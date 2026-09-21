using System.Windows.Controls;
using Wpf.Ui.Controls;

namespace GamePadT9;

internal static class SteamTouchpadSettingsValidation
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "artifacts", "steam-angles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using (var form = new SettingsForm(new(), value => value.Save(directory)))
            {
                form.Show(); form.ShowPage(4); await Task.Delay(120);
                var page = form.Control<SteamTouchpadSettings>("Steam2015Page");
                var left = (NumberBox)page.FindName("LeftValue"); var right = (NumberBox)page.FindName("RightValue");
                var mode = (System.Windows.Controls.ComboBox)page.FindName("AdjustmentMode");
                var slider = (System.Windows.Controls.Slider)page.FindName("RightSlider");
                check(page.IsVisible && page.ActualWidth > 400 && left.ActualHeight > 25 && mode.Items.Count == 2,
                    "2015 angle settings are visible in their own page with a two-choice dropdown.");
                check(!page.Linked && page.LeftAngle == 0 && page.RightAngle == 0, "Old preferences default to independent zero-degree layouts.");
                left.Value = -15; right.Value = 15;
                check(form.Draft.Steam2015LeftAngle == -15 && form.Draft.Steam2015RightAngle == 15,
                    "Independent signed angles reach the draft separately.");
                await Task.Delay(100); SavePreview(page, Path.Combine(root, "artifacts", "settings-steam-2015.png"));
                mode.SelectedIndex = 1;
                check(page.RightAngle == 15 && form.Draft.Steam2015LinkAngles, "Linking mirrors the right layout from the left.");
                slider.Value = 45;
                check(page.LeftAngle == -45 && page.RightAngle == 45 && left.Value == -45 && right.Value == 45,
                    "Editing the right linked slider mirrors the left numeric value.");
                left.Value = 30;
                check(page.LeftAngle == 30 && page.RightAngle == -30 && ((TouchpadLayoutPreview)page.FindName("RightPreview")).Angle == -30,
                    "Editing the left linked number mirrors the right angle and preview.");
                foreach (var angle in new[] { -180, 0, 180 })
                {
                    left.Value = angle;
                    check(page.LeftAngle == angle && page.RightAngle == -angle, "Mirroring preserves the full range, including zero: " + angle);
                }
                left.Value = -15;
                form.Draft.Save(directory);
                check(UserSettings.Load(directory, out _) == form.Draft, "Mirrored linked angles survive save and reload.");
                await Task.Delay(100); SavePreview(page, Path.Combine(root, "artifacts", "settings-steam-2015-mirrored.png"));
                slider.Value = 45;
                mode.SelectedIndex = 0; left.Value = -30;
                check(page.LeftAngle == -30 && page.RightAngle == 45, "Unlinking preserves each side and allows independent edits.");
                form.Click("SaveSettings");
            }
            var saved = UserSettings.Load(directory, out var warning);
            check(warning == null && saved.Steam2015LeftAngle == -30 && saved.Steam2015RightAngle == 45 && !saved.Steam2015LinkAngles,
                "Signed angles and adjustment mode persist across reloads.");
            using (var form = new SettingsForm(saved, _ => throw new Exception("Cancelled angles were saved.")))
            {
                form.Show(); form.ShowPage(4); var page = form.Control<SteamTouchpadSettings>("Steam2015Page");
                check(page.LeftAngle == -30 && page.RightAngle == 45, "Reopening restores both angle controls.");
                form.Click("ResetDefaults");
                check(form.Draft.Steam2015LeftAngle == 0 && form.Draft.Steam2015RightAngle == 0 && !form.Draft.Steam2015LinkAngles,
                    "Restore defaults resets angle values and linking.");
                form.Click("CancelSettings");
            }
            check(UserSettings.Load(directory, out _) == saved, "Cancel does not overwrite the saved touchpad angles.");
            File.WriteAllText(Path.Combine(directory, "user-settings.json"),
                "{\"Steam2015LinkAngles\":true,\"Steam2015LeftAngle\":25,\"Steam2015RightAngle\":25,\"PanelOpacity\":73}");
            var migrated = UserSettings.Load(directory, out warning);
            check(warning == null && migrated.Steam2015LinkAngles && migrated.Steam2015LeftAngle == 25 && migrated.Steam2015RightAngle == -25 && migrated.PanelOpacity == 73,
                "Earlier same-direction linking migrates to mirrored angles without resetting other preferences.");
            foreach (var json in new[] { "{\"Steam2015LeftAngle\":181}", "{\"Steam2015RightAngle\":-181}",
                "{\"Steam2015LinkAngles\":true,\"Steam2015LeftAngle\":15}" })
            {
                File.WriteAllText(Path.Combine(directory, "user-settings.json"), json);
                check(UserSettings.Load(directory, out warning) == new UserSettings() && warning != null,
                    "Invalid/out-of-sync touchpad angles are rejected on load.");
            }
        }
        finally { Directory.Delete(directory, true); }
    }
    private static void SavePreview(SteamTouchpadSettings page, string path)
    {
        // Render only our own controls, even when another app covers the window.
        page.UpdateLayout();
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(page.ActualWidth),
            (int)Math.Ceiling(page.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var background = new System.Windows.Media.DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 31, 33)),
                null, new System.Windows.Rect(0, 0, page.ActualWidth, page.ActualHeight));
        bitmap.Render(background); bitmap.Render(page);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
