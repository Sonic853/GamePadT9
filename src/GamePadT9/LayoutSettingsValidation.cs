using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Point = System.Windows.Point;

namespace GamePadT9;

internal static class LayoutSettingsValidation
{
    internal static async Task Run(string root, Action<bool, string> check)
    {
        var directory = Path.Combine(root, "artifacts", "layout-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalPointer = System.Windows.Forms.Cursor.Position;
        try
        {
            using (var form = new SettingsForm(new(), value => value.Save(directory)))
            {
                form.Topmost = true; form.Show(); form.Activate(); form.ShowPage(3);
                await Task.Delay(150); form.UpdateLayout();
                var pinyin = form.Control<LetterLayoutPicker>("PinyinLayoutPicker");
                var english = form.Control<LetterLayoutPicker>("EnglishLayoutPicker");
                var reuse = form.Control<System.Windows.Controls.CheckBox>("ReusePinyinLayout");
                async Task Click(FrameworkElement element)
                {
                    element.BringIntoView(); await Task.Delay(60); form.Activate();
                    if (TsfClient.GetForegroundWindow() != form.Handle) throw new Exception("Owned layout window lost focus before mouse input");
                    var point = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
                    SetCursorPos((int)point.X, (int)point.Y); await Task.Delay(30);
                    MouseButton(true); MouseButton(false); await Task.Delay(70);
                }
                async Task Drag(LetterLayoutPicker picker, int from, int? to, bool cancel = false)
                {
                    picker.BringIntoView(); await Task.Delay(80); form.Activate();
                    if (TsfClient.GetForegroundWindow() != form.Handle) throw new Exception("Owned layout window lost focus before dragging");
                    var cell = picker.CustomCell(from);
                    var start = cell.PointToScreen(new Point(cell.ActualWidth / 2, cell.ActualHeight / 2));
                    var target = to is int slot ? picker.CustomCell(slot) : (FrameworkElement)picker.Choice(0);
                    var end = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2));
                    SetCursorPos((int)start.X, (int)start.Y); await Task.Delay(30); MouseButton(true);
                    try
                    {
                        await Task.Delay(40); SetCursorPos((int)start.X + 9, (int)start.Y + 9); await Task.Delay(30);
                        SetCursorPos((int)end.X, (int)end.Y); await Task.Delay(60);
                        if (cancel) { SendKeys.SendWait("{ESC}"); await Task.Delay(40); }
                    }
                    finally { MouseButton(false); }
                    await Task.Delay(80);
                }
                check(pinyin.Choice(0).IsChecked == true && english.Choice(0).IsChecked == true && english.IsEnabled && reuse.IsChecked == false,
                    "Pinyin and English each start with an independent default four-cell selection");
                check(Enumerable.Range(0, 4).All(i => pinyin.Choice(i).Content is System.Windows.Controls.Primitives.UniformGrid &&
                    ((TextBlock)pinyin.Choice(i).Template.FindName("CustomCaption", pinyin.Choice(i))).Visibility == (i == 3 ? Visibility.Visible : Visibility.Collapsed)) &&
                    ((TextBlock)pinyin.CustomCell(0).Child).Text == "",
                    "Compact previews contain only the four cells, with an empty blank tile and a bottom caption only for custom");
                await Click(pinyin.Choice(1));
                check(form.Draft.PinyinOrder == "1243" && form.Draft.EnglishOrder == "4132", "The visible second preset changes only pinyin");
                var frame = (Border)pinyin.Choice(1).Template.FindName("Frame", pinyin.Choice(1));
                check(frame.BorderThickness == new Thickness(2) && frame.BorderBrush.ToString() == "#FF76E8B0",
                    "Selected layout has the requested highlight around all four sides");
                await Click(pinyin.Choice(2));
                check(form.Draft.PinyinOrder == "1234" && pinyin.Choice(1).IsChecked == false, "The third preview selects row order exclusively");
                await Click(pinyin.Choice(3)); await Drag(pinyin, 0, 1);
                check(form.Draft.PinyinOrder == "1432", "A real mouse drag swaps the source and destination custom tiles");
                await Drag(pinyin, 0, 0); await Drag(pinyin, 0, null);
                check(form.Draft.PinyinOrder == "1432", "Dropping on the same tile or outside the custom grid leaves its order unchanged");
                await Drag(pinyin, 0, 2, cancel: true);
                check(form.IsVisible && form.Draft.PinyinOrder == "1432" && Mouse.Captured == null, "Escape cancels a drag without closing settings or changing the order");
                await Click(english.Choice(3)); await Drag(english, 3, 0);
                check(form.Draft.EnglishOrder == "2134" && form.Draft.PinyinOrder == "1432", "English custom dragging does not alter pinyin's custom order");
                pinyin.BringIntoView(); await Task.Delay(80);
                PanelSnapshot.Save(form, Path.Combine(root, "artifacts", "settings-layout-custom.png"));
                await Click(reuse);
                check(!english.IsEnabled && form.Draft.EnglishOrder == "1432" && form.Draft.EnglishCustomOrder == "2134",
                    "Reuse disables English editing and preserves its independent custom order");
                await Click(pinyin.Choice(1));
                check(form.Draft.EnglishOrder == "1243" && english.Choice(1).IsChecked == true,
                    "Disabled English previews follow pinyin selection while reuse is enabled");
                await Click(english.Choice(2)); await Drag(english, 0, 1);
                check(form.Draft.EnglishLayout == LetterLayout.Custom && form.Draft.EnglishCustomOrder == "2134" && form.Draft.EnglishOrder == "1243",
                    "Clicks and drags cannot modify the disabled English layout");
                await Click(reuse);
                check(english.IsEnabled && english.Choice(3).IsChecked == true && form.Draft.EnglishOrder == "2134",
                    "Unchecking reuse restores the independent English selection and custom order");
                await Click(reuse); pinyin.BringIntoView(); await Task.Delay(80);
                PanelSnapshot.Save(form, Path.Combine(root, "artifacts", "settings-layout-reuse.png"));
                check(!File.Exists(Path.Combine(directory, "user-settings.json")), "Layout editing is a draft until settings are saved");
                form.Click("SaveSettings");
            }
            var persisted = UserSettings.Load(directory, out var warning);
            check(warning == null && persisted.PinyinLayout == LetterLayout.Clockwise && persisted.PinyinCustomOrder == "1432" &&
                persisted.EnglishLayout == LetterLayout.Custom && persisted.EnglishCustomOrder == "2134" && persisted.EnglishUsePinyinLayout,
                "Saving reloads both presets, both custom permutations and the reuse flag");
            using (var form = new SettingsForm(persisted, _ => throw new Exception("Cancel must not save")))
            {
                form.Show(); form.ShowPage(3);
                check(!form.Control<LetterLayoutPicker>("EnglishLayoutPicker").IsEnabled && form.Draft == persisted,
                    "Reopening settings restores the inherited layout and disabled English controls");
                form.Click("ResetDefaults");
                check(form.Draft == new UserSettings() && form.Control<LetterLayoutPicker>("EnglishLayoutPicker").IsEnabled,
                    "Restore defaults resets all layout orders and re-enables independent English settings");
                form.Click("CancelSettings");
            }
            check(UserSettings.Load(directory, out _) == persisted, "Cancel preserves saved custom layouts and reuse state");
            foreach (var invalid in new[] { "{\"PinyinCustomOrder\":\"1123\"}", "{\"EnglishCustomOrder\":null}", "{\"EnglishLayout\":99}" })
            {
                File.WriteAllText(Path.Combine(directory, "user-settings.json"), invalid);
                check(UserSettings.Load(directory, out warning) == new UserSettings() && warning != null, "Invalid layout settings fall back safely: " + invalid);
            }
        }
        finally
        {
            System.Windows.Forms.Cursor.Position = originalPointer;
            File.Delete(Path.Combine(directory, "user-settings.json")); Directory.Delete(directory);
        }
    }
    private static void MouseButton(bool down)
    {
        MouseInput[] inputs = [new() { Flags = down ? 2u : 4u }];
        if (SendInput(1, inputs, Marshal.SizeOf<MouseInput>()) != 1) throw new Exception("Owned-window mouse event failed");
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    {
        public uint Type;
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint Extra;
    }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, MouseInput[] input, int size);
}
