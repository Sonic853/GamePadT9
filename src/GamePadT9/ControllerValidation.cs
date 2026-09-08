using System.Runtime.InteropServices;
using System.Text.Json;

namespace GamePadT9;

// SDL virtual joysticks exist only inside this process. No driver, OS input or IME changes.
internal static class ControllerValidation
{
    internal static int Run(string root, RimeEngine engine)
    {
        var checks = new List<string>(); object[] hardware = []; string? error = null;
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add(name); }
        try
        {
            using var devices = new GamepadDevices();
            devices.Poll(null, out _, true);
            hardware = devices.Connected.Select(d => (object)new { d.Name, d.Family }).ToArray();
            using var glyphs = new ButtonGlyphs();
            foreach (var family in Enum.GetValues<GamepadFamily>())
            {
                Check(ButtonGlyphs.Buttons.All(button => glyphs.Get(family, button).GetDimensions() == new SizeF(32, 32)), $"{family}: all 16 Steam SVG glyphs are embedded and loadable");
                using var pad = new VirtualPad(family);
                devices.Poll(null, out _, true);
                var device = devices.Connected.Single(d => d.Instance == pad.Id);
                Check(device.Family == family, $"{family}: SDL detects the controller family from its hardware identity");
                Check(devices.Poll(device.Id, out var neutral) && neutral.Buttons == 0 && neutral.LT == 0 && neutral.RT == 0,
                    $"{family}: selected SDL gamepad starts with neutral buttons and released triggers");
                var controller = new Controller(); controller.Update(neutral, 0);
                var buttonActions = new (int Button, PadAction Action)[] { (0, PadAction.Confirm), (2, PadAction.Backspace), (3, PadAction.SwitchMode),
                    (9, PadAction.Previous), (10, PadAction.Next), (13, PadAction.PagePrevious), (14, PadAction.PageNext) };
                long time = 10;
                foreach (var mapping in buttonActions)
                {
                    pad.Button(mapping.Button, true); devices.Poll(device.Id, out var state);
                    Check(controller.Update(state, time++).Single().Action == mapping.Action, $"{family}: physical button {mapping.Button} maps to {mapping.Action}");
                    pad.Button(mapping.Button, false); devices.Poll(device.Id, out state); controller.Update(state, time++);
                }
                pad.Button(4, true); pad.Button(6, true); devices.Poll(device.Id, out var chord);
                Check(controller.Update(chord, time++).Single().Action == PadAction.Toggle, $"{family}: both menu buttons toggle input together");
                Check(controller.Update(chord, time++).Count == 0, $"{family}: holding the menu chord does not toggle again");
                pad.Button(4, false); pad.Button(6, false); devices.Poll(device.Id, out neutral); controller.Update(neutral, time++);
                pad.Button(1, true); devices.Poll(device.Id, out var cancel); controller.Update(cancel, time++);
                pad.Button(1, false); devices.Poll(device.Id, out neutral);
                Check(controller.Update(neutral, time++).Single().Action == PadAction.Cancel, $"{family}: short east-button press cancels candidates");
                pad.Button(1, true); devices.Poll(device.Id, out cancel); controller.Update(cancel, time);
                Check(controller.Update(cancel, time + 1000).Single().Action == PadAction.Disable, $"{family}: long east-button press disables input");
                pad.Button(1, false); devices.Poll(device.Id, out neutral); controller.Update(neutral, time + 1010); time += 1020;
                for (var region = 0; region < 9; region++)
                {
                    pad.Axis(2, (short)((region % 3 - 1) * 25000)); pad.Axis(3, (short)((region / 3 - 1) * 25000));
                    pad.Axis(5, short.MinValue); devices.Poll(device.Id, out var state); controller.Update(state, time++);
                    pad.Axis(5, short.MaxValue); devices.Poll(device.Id, out state);
                    var action = controller.Update(state, time++).Single();
                    Check(action.Action == PadAction.Region && action.Region == region && !action.StickClick,
                        $"{family}: SDL axes and trigger select 123/456/789 region {region + 1}");
                }
                pad.Axis(5, short.MinValue); devices.Poll(device.Id, out neutral); controller.Update(neutral, time++);
                pad.Button(8, true); devices.Poll(device.Id, out var click);
                Check(controller.Update(click, time++).Single().StickClick, $"{family}: right-stick click retains numeric zero");
                controller.Reset();
                Check(controller.Update(click, time++).Count == 0, $"{family}: selecting a controller with a held button does not type");
                controller.Configure(new() { Stick = ControlSide.Left, Trigger = ControlSide.Left });
                pad.Button(8, false); pad.Axis(0, -25000); pad.Axis(1, -25000); devices.Poll(device.Id, out neutral); controller.Update(neutral, time++);
                pad.Axis(4, short.MaxValue); devices.Poll(device.Id, out var left);
                Check(left.LT == 255 && left.LX == -25000 && left.LY == 25000 && controller.Update(left, time++).Single().Region == 0,
                    $"{family}: left stick and left trigger read from the selected SDL device");
                pad.Axis(4, short.MinValue); devices.Poll(device.Id, out neutral); controller.Update(neutral, time++);
                pad.Button(7, true); devices.Poll(device.Id, out left);
                Check(controller.Update(left, time++).Single().StickClick, $"{family}: left-stick click retains numeric zero after rebinding");

                var session = new InputSession(engine);
                using var overlay = new MainForm(session); overlay.UseDevice(device);
                overlay.Present(4, device.Instance); // Render the selected device name without opening an input target.
                using (var picture = overlay.CreateSnapshot()) picture.Save(Path.Combine(root, "artifacts", $"controller-{family}-t9.png"));
                // Mode changes require an enabled session, but no target is accessed for this rendering test.
                session.Enable(true).GetAwaiter().GetResult(); session.Handle(new(PadAction.SwitchMode)).GetAwaiter().GetResult();
                using (var picture = overlay.CreateSnapshot()) picture.Save(Path.Combine(root, "artifacts", $"controller-{family}-numeric.png"));
                session.Enable(false).GetAwaiter().GetResult();
            }

            using var ps4 = new VirtualPad(GamepadFamily.PS4);
            using var ps5 = new VirtualPad(GamepadFamily.PS5);
            devices.Poll(null, out _, true);
            var first = devices.Connected.Single(d => d.Instance == ps4.Id);
            var second = devices.Connected.Single(d => d.Instance == ps5.Id);
            ps4.Button(0, true); ps5.Button(2, true);
            Check(devices.Poll(second.Id, out var selected) && selected.Buttons == Buttons.X, "Selecting PS5 ignores the simultaneous PS4 input");
            Check(devices.Poll(first.Id, out selected) && selected.Buttons == Buttons.A, "Selecting PS4 ignores the simultaneous PS5 input");
            var savedId = second.Id; ps5.Disconnect();
            Check(!devices.Poll(savedId, out _, true) && devices.Active == null, "An explicitly selected unplugged device never falls back to another controller");
            using var reconnected = new VirtualPad(GamepadFamily.PS5);
            Check(devices.Poll(savedId, out _, true) && devices.Active?.Instance == reconnected.Id, "A reconnected selected device is found by persistent identity");
            Check(devices.Poll(null, out _) && devices.Active?.Instance == reconnected.Id, "Automatic selection retains the current controller");

            var directory = Path.Combine(root, "artifacts", "controller-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var preferences = new UserSettings { ControllerId = savedId, ControllerName = second.Name, ControllerFamily = GamepadFamily.PS5, PanelOpacity = 70 };
                preferences.Save(directory);
                Check(UserSettings.Load(directory, out _) == preferences, "Selected device and existing opacity settings survive save/reload");
                using var form = new SettingsForm(preferences, value => value.Save(directory), () => devices.Connected);
                form.Show(); Application.DoEvents();
                var picker = (ComboBox)form.Controls.Find("ControllerSelector", true).Single();
                Check(picker.Items.Count == devices.Connected.Count + 1 && form.Draft.ControllerId == savedId, "Settings lists connected controllers plus automatic selection and restores the saved choice");
                reconnected.Disconnect(); devices.Poll(savedId, out _, true); form.RefreshDevices();
                Check(picker.Text.Contains("未连接") && form.Draft.ControllerId == savedId, "Hot unplug updates the picker without losing its selected device");
                picker.SelectedIndex = 0;
                Check(form.Draft.ControllerId == null, "Settings can return to automatic device selection");
                form.Close();
            }
            finally { foreach (var path in Directory.EnumerateFiles(directory)) File.Delete(path); Directory.Delete(directory); }
            Check(GamepadDevices.InvertY(short.MinValue) == short.MaxValue && GamepadDevices.Trigger(short.MaxValue) == 255 && GamepadDevices.Trigger(-1) == 0,
                "Axis normalization clamps extreme values without wrapping or spurious trigger input");
        }
        catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
        var report = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = error == null, hardware, checks, error }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artifacts", "controller-verification.json"), report); Console.WriteLine(report);
        return error == null ? 0 : 1;
    }

    private sealed class VirtualPad : IDisposable
    {
        internal uint Id { get; }
        private nint joystick;
        private bool attached = true;
        internal VirtualPad(GamepadFamily family)
        {
            var name = Marshal.StringToCoTaskMemUTF8("GamePad T9 test " + family);
            try
            {
                var desc = new VirtualDesc
                {
                    Version = (uint)Marshal.SizeOf<VirtualDesc>(), Type = 1,
                    Vendor = family == GamepadFamily.Xbox ? (ushort)0x045e : (ushort)0x054c,
                    Product = family switch { GamepadFamily.PS4 => 0x05c4, GamepadFamily.PS5 => 0x0ce6, _ => 0x02ea },
                    Axes = 6, Buttons = 15, ButtonMask = 0x7fff, AxisMask = 0x3f, Name = name
                };
                Id = SDL_AttachVirtualJoystick(in desc);
                if (Id == 0) throw new Exception(Sdl.Error);
                joystick = SDL_OpenJoystick(Id); if (joystick == 0) throw new Exception(Sdl.Error);
                Axis(4, short.MinValue); Axis(5, short.MinValue);
            }
            catch { if (Id != 0) SDL_DetachVirtualJoystick(Id); throw; }
            finally { Marshal.FreeCoTaskMem(name); }
        }
        internal void Button(int index, bool down) { if (!SDL_SetJoystickVirtualButton(joystick, index, down)) throw new Exception(Sdl.Error); }
        internal void Axis(int index, short value) { if (!SDL_SetJoystickVirtualAxis(joystick, index, value)) throw new Exception(Sdl.Error); }
        internal void Disconnect() { if (attached) { if (!SDL_DetachVirtualJoystick(Id)) throw new Exception(Sdl.Error); attached = false; } }
        public void Dispose() { Disconnect(); if (joystick != 0) { SDL_CloseJoystick(joystick); joystick = 0; } }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct VirtualDesc
    {
        internal uint Version;
        internal ushort Type, Padding, Vendor, Product, Axes, Buttons, Balls, Hats, TouchpadsCount, SensorsCount, Padding2, Padding3;
        internal uint ButtonMask, AxisMask;
        internal nint Name, Touchpads, Sensors, Userdata, Update, SetPlayerIndex, Rumble, RumbleTriggers, SetLed, SendEffect, SetSensorsEnabled, Cleanup;
    }
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern uint SDL_AttachVirtualJoystick(in VirtualDesc desc);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_DetachVirtualJoystick(uint id);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern nint SDL_OpenJoystick(uint id);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern void SDL_CloseJoystick(nint joystick);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_SetJoystickVirtualButton(nint joystick, int button, [MarshalAs(UnmanagedType.I1)] bool down);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SDL_SetJoystickVirtualAxis(nint joystick, int axis, short value);
}
