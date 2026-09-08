using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GamePadT9;

// Only owned WPF windows are changed. Profile readback is written by the target's
// own C# TSF observer; it does not depend on the switching helper's response.
internal sealed class InputMethodValidation : Form
{
    private sealed record Editor(Process Process, InputWindow Window, AutomationElement Field, string Snapshot);
    private readonly string root;
    private readonly RimeEngine engine;
    private readonly bool x86;
    private readonly List<Editor> editors = [];
    private readonly List<InputSession> sessions = [];
    private readonly List<string> checks = [];
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    internal InputMethodValidation(string root, RimeEngine engine, bool x86)
    {
        this.root = root; this.engine = engine; this.x86 = x86;
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        Shown += async (_, _) => {
            string? error = null;
            try { await Run(); Result = 0; }
            catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
            finally
            {
                foreach (var session in sessions) await session.ShutdownAsync();
                foreach (var editor in editors) { if (!editor.Process.HasExited) editor.Process.CloseMainWindow(); editor.Process.Dispose(); }
                var report = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = Result == 0,
                    architecture = x86 ? "x86" : "x64", checks, syntheticKeyboardEvents = NumericInput.SentKeyEvents,
                    fallback = "Standalone absence simulated for this request; installed profiles were not removed or disabled.",
                    scope = "Only owned test windows; independent profile readback from target TSF manager", error }, Json);
                File.WriteAllText(Path.Combine(root, "artifacts", $"input-method-{(x86 ? "x86" : "x64")}-verification.json"), report);
                Console.WriteLine(report); Close();
            }
        };
    }
    private void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks.Add(description); }
    private async Task<Editor> OpenEditor()
    {
        var file = Path.Combine(root, "artifacts", "InputMethod-" + Guid.NewGuid().ToString("N") + ".txt");
        var folder = "test-editor-installed" + (x86 ? "-x86" : "");
        var info = new ProcessStartInfo(Path.Combine(root, "artifacts", folder, "TestEditor.exe")) { UseShellExecute = false };
        info.ArgumentList.Add(file); info.ArgumentList.Add("--report-profile");
        var process = Process.Start(info) ?? throw new IOException("Test editor did not start.");
        try
        {
            for (var i = 0; i < 80 && !File.Exists(file + ".profile.json"); i++) await Task.Delay(50);
            process.Refresh();
            var handle = process.MainWindowHandle;
            var thread = TsfClient.GetWindowThreadProcessId(handle, out var pid);
            var window = AutomationElement.FromHandle(handle);
            var field = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (field == null || thread == 0) throw new Exception("Test text field missing.");
            var editor = new Editor(process, new(handle, pid, thread), field, file + ".profile.json");
            editors.Add(editor); return editor;
        }
        catch { if (!process.HasExited) process.CloseMainWindow(); process.Dispose(); throw; }
    }
    private static async Task Focus(Editor editor) { editor.Field.SetFocus(); await Task.Delay(100); }
    private static InputProfile Read(Editor editor)
    {
        using var stream = new FileStream(editor.Snapshot, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<InputProfile>(stream, Json)!;
    }
    private static async Task Expect(Editor editor, InputProfile profile)
    {
        for (var i = 0; i < 60; i++) { if (Read(editor) == profile) return; await Task.Delay(50); }
        throw new Exception("Unexpected profile in target: " + JsonSerializer.Serialize(Read(editor)) + "; expected " + JsonSerializer.Serialize(profile));
    }
    private static async Task Set(InputMethodSwitcher control, Editor editor, InputProfile profile)
    {
        var result = await control.RunAsync(editor.Window, "restore", profile);
        if (!result.Ok) throw new Exception("Could not prepare test input profile: " + result.Error);
        await Expect(editor, profile);
    }
    private static readonly InputProfile Standalone = new(1, 0x0804, new("595B67E9-48A3-4C82-B7B1-64E4A35C9D92"), new("79C457D1-690A-4F83-A3DE-C95C98E01D4D"), "0");
    private static readonly InputProfile Xiaobai = new(1, 0x0804, new("A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A"), new("3D02CAB6-2B8E-4781-BA20-1C9267529467"), "0");
    private async Task Run()
    {
        var control = new InputMethodSwitcher(root);
        var first = await OpenEditor();
        var layouts = new nint[GetKeyboardLayoutList(0, null)]; GetKeyboardLayoutList(layouts.Length, layouts);
        var english = new InputProfile(2, 0x0409, Guid.Empty, Guid.Empty, layouts.First(h => ((long)h & 0xffff) == 0x0409).ToString("X"));
        await Set(control, first, english); await Focus(first);
        var session = new InputSession(engine, control); sessions.Add(session);
        using var overlay = new MainForm(session);
        var pad = new Controller(); long tick = 0; pad.Update(default, tick++);
        async Task Chord()
        {
            pad.Update(default, tick++);
            foreach (var action in pad.Update(new Gamepad { Buttons = Buttons.View | Buttons.Menu }, tick++)) await session.Handle(action);
            pad.Update(default, tick++);
        }
        await Chord(); await Expect(first, Standalone);
        Check(session.Enabled && overlay.Visible && TsfClient.GetForegroundWindow() == first.Window.Handle, "View + Menu selects GamePad T9 and shows overlay without stealing focus");
        foreach (var region in new[] { 5, 3, 3, 1, 5 }) await session.Handle(new(PadAction.Region, region));
        if (!Validation.Find(engine, "你好")) throw new Exception("nihao missing.");
        await session.Handle(new(PadAction.Confirm)); await Task.Delay(100);
        Check(first.Field.TryGetCurrentPattern(ValuePattern.Pattern, out var text) && ((ValuePattern)text).Current.Value == "你好", "Chinese input works immediately after automatic profile activation");
        await session.Handle(new(PadAction.Cancel)); await Expect(first, Standalone);
        Check(session.Enabled, "Short B keeps the selected input method");
        pad.Update(new Gamepad { Buttons = Buttons.B }, tick++); tick += 1001;
        foreach (var action in pad.Update(new Gamepad { Buttons = Buttons.B }, tick++)) await session.Handle(action);
        await Expect(first, english);
        Check(!session.Enabled && !overlay.Visible && !control.HasSavedProfiles, "Long B restores the original English keyboard layout and hides the overlay");
        pad.Update(default, tick++);

        await Set(control, first, Xiaobai); await Focus(first);
        await Chord(); await Expect(first, Standalone);
        var second = await OpenEditor();
        await Set(control, second, english); await Focus(second); await session.CheckFocus(); await Expect(second, Standalone);
        await Focus(first); await session.CheckFocus();
        await Chord();
        await Expect(first, Xiaobai); await Expect(second, english);
        Check(!control.HasSavedProfiles, "Each visited window restores its own original profile; revisiting does not overwrite snapshots");

        await Set(control, first, Standalone); await Focus(first); await Chord(); await Chord(); await Expect(first, Standalone);
        Check(!control.HasSavedProfiles, "An already-selected GamePad T9 profile is preserved on close");

        await Set(control, first, english); await Focus(first);
        var pending = session.Handle(new(PadAction.Toggle));
        await session.Handle(new(PadAction.Toggle)); await pending; await Expect(first, english);
        Check(!session.Enabled, "Closing during activation restores the original profile after the transition completes");

        var fallbackControl = new InputMethodSwitcher(root, simulateMissingStandalone: true);
        var fallback = new InputSession(engine, fallbackControl); sessions.Add(fallback);
        await fallback.Handle(new(PadAction.Toggle)); await Expect(first, Xiaobai);
        Check(fallback.Enabled && TsfClient.FindTarget()?.Backend == InputBackend.Xiaobai, "If GamePad T9 is unavailable, activation falls back to xiaobai");
        await fallback.ShutdownAsync(); await Expect(first, english);
        Check(!fallbackControl.HasSavedProfiles, "Orderly shutdown restores the original profile");
        Check(NumericInput.SentKeyEvents == 0, "Switching, restoring, and Chinese typing produce zero simulated keyboard events");
    }
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
}
