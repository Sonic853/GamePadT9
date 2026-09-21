using System.Text.Json;

namespace GamePadT9;

internal static class SteamControllerValidation
{
    internal static int Run()
    {
        var checks = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks.Add(message); }
        foreach (var steamFamily in new[] { GamepadFamily.SteamController, GamepadFamily.SteamController2015 })
        {
        using var devices = new GamepadDevices();
        using var pad = new ControllerValidation.VirtualPad(steamFamily);
        devices.Poll(null, out _, true);
        var device = devices.Connected.Single(d => d.Instance == pad.Id);
        Check(device.Family == steamFamily && device.Touchpads == 2, steamFamily + " is identified with two touchpads.");
        Gamepad Read() { if (!devices.Poll(device.Id, out var state)) throw new Exception("Virtual Steam Controller disconnected."); return state; }
        var selection = new EnglishSelection(); var controller = new Controller(selection); long time = 0;
        List<PadEvent> Sample() => controller.Update(Read(), time++);
        Sample();
        foreach (var side in Enum.GetValues<ControlSide>())
        {
            controller.Configure(new() { Stick = side });
            var index = side == ControlSide.Left ? 0 : 1;
            for (var region = 0; region < 9; region++)
            {
                pad.Touch(index, true, (region % 3) / 2f, (region / 3) / 2f);
                pad.Axis(5, short.MinValue); Sample();
                pad.Axis(5, short.MaxValue); var events = Sample();
                Check(events is [{ Action: PadAction.Region }] && events[0].Region == region, $"{side} touchpad selects region {region + 1} through the trigger.");
                Check(Sample().Count == 0, $"{side}/{region + 1}: holding the trigger cannot repeat a letter.");
            }
            pad.Axis(5, short.MinValue); pad.Touch(index, false, 1, 1); Sample();
            Check(controller.Region == 4 && Controller.IsNeutral(Read()), $"{side}: lifting a finger discards stale pad coordinates and returns to centre.");
            var click = side == ControlSide.Left ? Sdl.TouchpadButton : Sdl.Misc2Button;
            pad.Touch(index, true, 0, 0); pad.Button(click, true);
            var action = Sample().Single();
            Check(action.StickClick && action.Region == 0, $"{side}: a touch and click in the same report select the new position with stick-click semantics.");
            Check(Sample().Count == 0, $"{side}: a held pad click does not repeat.");
            pad.Button(click, false); pad.Touch(index, false); Sample();
            pad.Axis(index * 2, 23000); pad.Axis(index * 2 + 1, 23000); Sample();
            pad.Touch(index, true, 0.5f, 0.5f); Sample();
            Check(controller.Region == 4, $"{side}: a centred touch overrides a deflected physical stick.");
            pad.Touch(index, false); Sample();
            var expected = steamFamily == GamepadFamily.SteamController2015 && side == ControlSide.Right ? 4 : 8;
            Check(controller.Region == expected, $"{steamFamily}/{side}: release restores a real stick or clears the legacy pad axes.");
            pad.Axis(index * 2, 0); pad.Axis(index * 2 + 1, 0); Sample();
        }
        controller.Configure(new()); controller.ConfigureEnglish(true); Sample();
        pad.Touch(1, true, 0.5f, 0); Sample(); pad.Axis(5, short.MaxValue);
        Check(selection.Consume(Sample().Single()) && selection.Group == 1, "Right touchpad alone opens the English ABC group.");
        pad.Axis(5, short.MinValue); pad.Touch(1, true, 1, 0); pad.Touch(0, true, 0, 1); Sample();
        pad.Axis(5, short.MaxValue); var letter = Sample().Single();
        Check(letter.Detail == 2 && T9Layout.EnglishText(letter) == "c", "Left touchpad C overrides right touchpad A inside the English group.");
        pad.Axis(5, short.MinValue); pad.Touch(0, true, 0, 0); Sample(); pad.Axis(5, short.MaxValue);
        Check(T9Layout.EnglishText(Sample().Single()) == "", "A left-pad blank overrides a valid right-pad letter without typing.");
        pad.Axis(5, short.MinValue); pad.Touch(0, false); pad.Touch(1, false); Sample();
        Check(controller.Detail == -1 && selection.Group == 1, "Releasing both pads keeps an opened English group with no highlighted letter.");
        pad.Axis(5, short.MaxValue);
        Check(selection.Consume(Sample().Single()) && selection.Group == null, "Confirming without a touched letter leaves the English group.");
        pad.Axis(5, short.MinValue); Sample(); controller.ConfigureEnglish(false);
        pad.Touch(1, true, 0.5f, 0); pad.Touch(0, true, 1, 1); Sample(); pad.Axis(5, short.MaxValue);
        letter = Sample().Single();
        Check(letter.Region == 1 && letter.Detail == 1 && T9Layout.Key(letter) == 'b', "Chinese mixed input combines the right pad group and left pad letter.");
        pad.Axis(5, short.MinValue); pad.Touch(0, false); pad.Touch(1, false); Sample();
        devices.Configure(new() { Steam2015LeftAngle = 90, Steam2015RightAngle = -90 });
        pad.Touch(0, true, 1, 0.5f); pad.Touch(1, true, 0, 0.5f);
        var turned = Read();
        Check(steamFamily == GamepadFamily.SteamController2015
            ? Math.Abs((int)turned.LX) < 2 && turned.LY > 30000 && Math.Abs((int)turned.RX) < 2 && turned.RY > 30000
            : turned.LX > 30000 && Math.Abs((int)turned.LY) < 2 && turned.RX < -30000,
            steamFamily + ": independent angles affect only 2015 touchpads, with clockwise layout orientation.");
        if (steamFamily == GamepadFamily.SteamController2015)
        {
            foreach (var degrees in new[] { -180, -45, 0, 30, 90, 180 })
            {
                devices.Configure(new() { Steam2015LeftAngle = degrees, Steam2015RightAngle = -degrees, Steam2015LinkAngles = true });
                var radians = degrees * Math.PI / 180;
                // Finger at the physical position of the rotated top-centre cell.
                var x = (float)(0.5 + 0.4 * Math.Sin(radians)); var y = (float)(0.5 - 0.4 * Math.Cos(radians));
                pad.Touch(0, true, x, y); pad.Touch(1, true, 1 - x, y); var state = Read();
                Check(Math.Abs((int)state.LX) < 3 && state.LY > 25000 && Math.Abs((int)state.RX) < 3 && state.RY > 25000,
                    $"2015: mirrored {degrees}/{-degrees} degree layouts select top centre from mirrored finger positions.");
            }
            pad.Touch(0, false, 1, 1); pad.Touch(1, false, 1, 1); pad.Axis(0, 17000); pad.Axis(1, -12000);
            var stick = Read();
            Check(stick.LX == 17000 && stick.LY == 12000 && stick.RX == 0 && stick.RY == 0,
                "2015: rotation never alters the physical stick or retains released pad coordinates.");
            pad.Axis(0, 0); pad.Axis(1, 0);
            pad.Touch(0, true, 0, 0.5f); pad.Button(13, true); pad.Button(Sdl.TouchpadButton, true);
            Check(Read().Buttons == Buttons.L3, "2015: a left-pad directional click cannot also page candidates.");
            pad.Button(13, false); pad.Button(Sdl.TouchpadButton, false); pad.Touch(0, false);
            pad.Button(17, true); Check(Read().Buttons == Buttons.Left, "2015: left grip pages backwards.");
            pad.Button(17, false); pad.Button(16, true); Check(Read().Buttons == Buttons.Right, "2015: right grip pages forwards.");
            pad.Button(16, false);
        }
        foreach (var family in new[] { GamepadFamily.Xbox, GamepadFamily.PS4, GamepadFamily.PS5 })
        {
            using var other = new ControllerValidation.VirtualPad(family); devices.Poll(null, out _, true);
            var otherDevice = devices.Connected.Single(d => d.Instance == other.Id);
            other.Touch(0, true, 0, 0); other.Button(Sdl.TouchpadButton, true);
            devices.Poll(otherDevice.Id, out var state);
            Check(otherDevice.Family == family && !state.LeftTouch && state.LX == 0 && state.Buttons == 0, family + ": its touchpad is not treated as a Steam stick or click.");
        }
        Check(GamepadDevices.Family(1, 0x28de, 0x11ff, 2) == GamepadFamily.Xbox, "Steam Input's virtual controller is not misidentified as physical Triton hardware.");
        foreach (ushort product in new ushort[] { 0x1302, 0x1304, 0x1305 })
            Check(GamepadDevices.Family(1, 0x28de, product, 2) == GamepadFamily.SteamController, $"Triton USB or receiver product {product:x4} is supported.");
        foreach (ushort product in new ushort[] { 0x1102, 0x1105, 0x1106, 0x1142 })
            Check(GamepadDevices.Family(1, 0x28de, product, 2) == GamepadFamily.SteamController2015, $"2015 USB/BLE/receiver product {product:x4} is supported.");
        using var glyphs = new ButtonGlyphs();
        foreach (var button in ButtonGlyphs.Buttons) Check(glyphs.Get(steamFamily, button).GetDimensions() == new SizeF(32, 32), steamFamily + " SVG loads: " + button);
        pad.Disconnect();
        Check(!devices.Poll(device.Id, out var disconnected, true) && Controller.IsNeutral(disconnected), "Disconnecting the chosen Steam Controller clears its input instead of selecting another device.");
        }
        Console.WriteLine(JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
