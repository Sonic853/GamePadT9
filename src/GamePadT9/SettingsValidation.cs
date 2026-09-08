using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GamePadT9;

// Exercises real settings controls and Windows' composited pixels over an owned backdrop.
internal sealed class SettingsValidation : Form
{
    private readonly string root;
    private readonly RimeEngine engine;
    private readonly List<string> checks = [];
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    internal SettingsValidation(string root, RimeEngine engine)
    {
        this.root = root; this.engine = engine;
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        Shown += async (_, _) =>
        {
            string? error = null;
            try { await Run(); Result = 0; }
            catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
            var report = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = Result == 0, checks, error }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(root, "artifacts", "settings-verification.json"), report);
            Console.WriteLine(report); Close();
        };
    }
    private void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
    private async Task Run()
    {
        VerifySvgGlyphs();
        foreach (var stick in Enum.GetValues<ControlSide>()) foreach (var trigger in Enum.GetValues<ControlSide>())
        {
            var controller = new Controller(); controller.Configure(new() { Stick = stick, Trigger = trigger });
            controller.Update(default, 0);
            var input = new Gamepad { LX = -22000, LY = 22000, RX = 22000, RY = -22000 };
            var unused = trigger == ControlSide.Left ? input with { RT = 255 } : input with { LT = 255 };
            Check(controller.Update(unused, 1).Count == 0, $"{stick}/{trigger}: the unselected trigger does not input");
            controller.Update(input, 2);
            var pressed = trigger == ControlSide.Left ? input with { LT = 255 } : input with { RT = 255 };
            var events = controller.Update(pressed, 3);
            Check(events.Count == 1 && events[0].Region == (stick == ControlSide.Left ? 0 : 8) && !events[0].StickClick,
                $"{stick}/{trigger}: chosen stick and trigger select the correct region");
            controller.Update(input, 4);
            Check(controller.Update(input with { Buttons = stick == ControlSide.Left ? Buttons.R3 : Buttons.L3 }, 5).Count == 0,
                $"{stick}/{trigger}: the unselected stick click does not input");
            controller.Update(input, 6);
            Check(controller.Update(input with { Buttons = stick == ControlSide.Left ? Buttons.L3 : Buttons.R3 }, 7).Single().StickClick,
                $"{stick}/{trigger}: chosen stick click retains numeric-zero semantics");
        }
        var changed = new Controller(); changed.Update(default, 0); changed.Configure(new() { Stick = ControlSide.Left, Trigger = ControlSide.Left });
        Check(changed.Update(new Gamepad { LT = 255, Buttons = Buttons.L3 }, 1).Count == 0, "Changing bindings with held controls cannot accidentally input");

        var directory = Path.Combine(root, "artifacts", "settings-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var defaults = UserSettings.Load(directory, out var warning);
            Check(warning == null && defaults == new UserSettings(), "Missing preferences use right controls and 50/80/90 visibility");
            var saved = false;
            using (var form = new SettingsForm(defaults, value => { value.Save(directory); saved = true; }))
            {
                form.Topmost = true; form.Show();
                await form.Dispatcher.InvokeAsync(form.UpdateLayout, System.Windows.Threading.DispatcherPriority.ContextIdle);
                await Task.Delay(250);
                Check(IsWindowVisible(form.Handle), "Settings window is visible even when the tray host starts hidden");
                Check(form.Control<System.Windows.Controls.ComboBox>("ControllerSelector").ActualWidth > 100 &&
                    form.Control<Wpf.Ui.Controls.Button>("SaveSettings").ActualHeight > 20,
                    "WPF settings content and footer are rendered and laid out");
                Check(form.Control<System.Windows.Controls.ComboBox>("StickSelector").SelectedIndex == 1 &&
                    form.Control<System.Windows.Controls.ComboBox>("TriggerSelector").SelectedIndex == 1, "Settings controls display the current left/right selection");
                var keyboardSelector = form.Control<System.Windows.Controls.ComboBox>("StickSelector");
                form.Activate(); keyboardSelector.Focus(); await Task.Delay(80);
                Check(GetForegroundWindow() == form.Handle && keyboardSelector.IsKeyboardFocusWithin, "Modeless WPF controls acquire keyboard focus from the tray host");
                SendKeys.SendWait("{UP}"); await Task.Delay(80);
                Check(keyboardSelector.SelectedIndex == 0, "Native arrow keys navigate the WPF dropdown through the WinForms message loop");
                keyboardSelector.SelectedIndex = 1;
                PanelSnapshot.Save(form, Path.Combine(root, "artifacts", "settings-window.png"));
                form.Control<System.Windows.Controls.ComboBox>("StickSelector").SelectedIndex = 0;
                form.Control<System.Windows.Controls.ComboBox>("TriggerSelector").SelectedIndex = 1;
                form.ShowPage(1); await Task.Delay(150);
                form.Control<Wpf.Ui.Controls.NumberBox>("OpacityValue0").Value = 20;
                form.Control<System.Windows.Controls.Slider>("OpacitySlider1").Value = 65;
                form.Control<Wpf.Ui.Controls.NumberBox>("OpacityValue2").Value = 95;
                Check(form.Control<Wpf.Ui.Controls.NumberBox>("OpacityValue0").Text == "20" &&
                    form.Control<Wpf.Ui.Controls.NumberBox>("OpacityValue1").Text == "65", "Percentage fields display the values changed by typing and sliders");
                await Task.Delay(150); PanelSnapshot.Save(form, Path.Combine(root, "artifacts", "settings-appearance-window.png"));
                form.Click("SaveSettings");
            }
            var persisted = UserSettings.Load(directory, out warning);
            Check(saved && warning == null && persisted == new UserSettings { Stick = ControlSide.Left, Trigger = ControlSide.Right, PanelOpacity = 20, GridOpacity = 65, HighlightOpacity = 95 },
                "Saving real settings controls persists independent bindings and visibility across reloads");
            using (var cancel = new SettingsForm(persisted, _ => throw new Exception("Cancel unexpectedly saved")))
            {
                cancel.Show(); cancel.Click("ResetDefaults");
                Check(cancel.Draft == defaults, "Restore defaults resets the complete settings draft");
                cancel.Activate(); await Task.Delay(80);
                Check(GetForegroundWindow() == cancel.Handle, "Cancel keyboard check targets only its owned settings window");
                SendKeys.SendWait("{ESC}"); await Task.Delay(80);
                Check(!cancel.IsVisible, "Escape closes the WPF settings window without saving");
            }
            Check(UserSettings.Load(directory, out _) == persisted, "Cancel leaves the saved settings unchanged");
            using (var failedSave = new SettingsForm(persisted, _ => throw new IOException("Verification: file is not writable")))
            {
                failedSave.Show(); failedSave.Click("SaveSettings");
                Check(failedSave.IsVisible && failedSave.Draft == persisted && failedSave.Control<Wpf.Ui.Controls.InfoBar>("SaveError").IsOpen,
                    "Failed saves retain the draft and show an inline error instead of closing the panel");
                failedSave.Click("CancelSettings");
            }
            File.WriteAllText(Path.Combine(directory, "user-settings.json"), "{\"GridOpacity\":150}");
            Check(UserSettings.Load(directory, out warning) == defaults && warning != null, "Invalid settings fall back without preventing startup");

            var backdropColor = Color.FromArgb(90, 110, 150);
            var start = new ProcessStartInfo(Path.Combine(root, "artifacts", "test-editor-installed", "TestEditor.exe")) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("GamePadT9 visibility test"); start.ArgumentList.Add("--backdrop");
            using var backdrop = Process.Start(start) ?? throw new Exception("Could not start the owned visual test window.");
            try
            {
                for (var i = 0; i < 80 && backdrop.MainWindowHandle == 0; i++) { await Task.Delay(50); backdrop.Refresh(); }
                var backdropHandle = backdrop.MainWindowHandle;
                if (backdropHandle == 0) throw new Exception("Owned visual test window missing.");
                var session = new InputSession(engine);
                using var overlay = new MainForm(session);
                await session.Enable(true); overlay.Present(4, 0);
                // Keep both owned surfaces visible without requiring permission to activate a window.
                SetWindowPos(backdropHandle, new nint(-1), overlay.Left - 10, overlay.Top - 10, overlay.Width + 20, overlay.Height + 20, 0x10);
                SetWindowPos(overlay.Handle, new nint(-1), 0, 0, 0, 0, 0x13);
                await Task.Delay(200);
                using (var layer = overlay.CreateSnapshot())
                using (var screen = new Bitmap(overlay.Width, overlay.Height))
                {
                    using (var g = Graphics.FromImage(screen)) g.CopyFromScreen(overlay.Location, Point.Empty, screen.Size);
                    layer.Save(Path.Combine(root, "artifacts", "overlay-transparency-layer.png"));
                    screen.Save(Path.Combine(root, "artifacts", "overlay-transparency.png"));
                    var samples = new[] { (new Point(350, 110), 128, Color.FromArgb(20, 24, 29)),
                        (new Point(32, 106), 204, Color.FromArgb(33, 39, 46)), (new Point(136, 212), 230, Color.FromArgb(110, 236, 169)) };
                    foreach (var (designPoint, alpha, color) in samples)
                    {
                        var point = new Point(designPoint.X * layer.Width / 780, designPoint.Y * layer.Height / 520);
                        Check(layer.GetPixel(point.X, point.Y).A == alpha, $"Layer alpha is independently {alpha}/255");
                        var actual = screen.GetPixel(point.X, point.Y);
                        int Blend(int foreground, int background) => (foreground * alpha + background * (255 - alpha) + 127) / 255;
                        Check(Math.Abs(actual.R - Blend(color.R, backdropColor.R)) <= 3 && Math.Abs(actual.G - Blend(color.G, backdropColor.G)) <= 3 && Math.Abs(actual.B - Blend(color.B, backdropColor.B)) <= 3,
                            $"Windows composes the {alpha}/255 region against the real window underneath");
                    }
                }
                overlay.ApplySettings(persisted);
                using (var custom = overlay.CreateSnapshot())
                    Check(custom.GetPixel(350 * custom.Width / 780, 110 * custom.Height / 520).A == 51 &&
                        custom.GetPixel(32 * custom.Width / 780, 106 * custom.Height / 520).A == 166 &&
                        custom.GetPixel(136 * custom.Width / 780, 212 * custom.Height / 520).A == 242,
                        "Changing visibility applies independently without multiplying region alpha");
                await session.Enable(false);
                Check(!overlay.Visible, "Transparent overlay still hides when input is disabled");
            }
            finally { if (!backdrop.HasExited) backdrop.CloseMainWindow(); }
        }
        finally { File.Delete(Path.Combine(directory, "user-settings.json")); Directory.Delete(directory); }
    }
    private void VerifySvgGlyphs()
    {
        var resources = typeof(ButtonGlyphs).Assembly.GetManifestResourceNames().Where(name => name.StartsWith("GamePadT9.Assets.Steam.")).ToArray();
        Check(resources.Length == 34 && resources.All(name => name.EndsWith(".svg")), "All 34 runtime glyph resources are SVG, with no PNG fallback");
        using var glyphs = new ButtonGlyphs();
        foreach (var family in Enum.GetValues<GamepadFamily>()) foreach (var scale in new[] { 1f, 1.25f, 1.5f, 2f })
        {
            foreach (var button in ButtonGlyphs.Buttons)
            {
                var side = (int)(48 * scale);
                using var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent); g.ScaleTransform(scale, scale);
                    g.SetClip(new RectangleF(0, 0, 48, 48));
                    using var transform = g.Transform;
                    var clip = g.ClipBounds; var smoothing = g.SmoothingMode;
                    glyphs.DrawIcon(g, family, button, new(8, 8, 24, 24));
                    using var after = g.Transform;
                    if (!transform.Elements.SequenceEqual(after.Elements) || g.ClipBounds != clip || g.SmoothingMode != smoothing)
                        throw new Exception($"{family}/{button}: SVG rendering leaked its graphics state at {scale * 100}%");
                }
                var visible = 0;
                for (var y = 0; y < side; y++) for (var x = 0; x < side; x++)
                {
                    if (bitmap.GetPixel(x, y).A == 0) continue;
                    visible++;
                    if (x < 8 * scale || y < 8 * scale || x >= 32 * scale || y >= 32 * scale)
                        throw new Exception($"{family}/{button}: SVG escaped its destination bounds at {scale * 100}%");
                }
                if (visible < 10) throw new Exception($"{family}/{button}: blank SVG rendering at {scale * 100}%");
                if (family == GamepadFamily.Xbox && button == "A" && scale == 2)
                {
                    if (bitmap.GetPixel(40, 24).ToArgb() != Color.FromArgb(89, 191, 64).ToArgb())
                        throw new Exception("SVG rendering did not preserve the original Steam Xbox green fill");
                }
            }
            Check(true, $"{family} at {scale * 100}%: all 16 SVGs render, preserve transparency and stay within bounds without changing caller graphics state");
        }
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
}
