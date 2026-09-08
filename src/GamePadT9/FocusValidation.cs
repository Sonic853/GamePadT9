using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace GamePadT9;

// Uses owned WPF editors and the production coordinator. Does not change saved user preferences.
internal sealed class FocusValidation : Form
{
    private readonly string root;
    private readonly RimeEngine engine;
    private readonly bool x86;
    private readonly List<string> checks = [];
    private readonly List<Process> editors = [];
    private InputSession? active;
    private MainForm? overlay;
    private bool released = true;
    private InputWindow targetWindow;
    private AutomationElement targetField = null!;
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    internal FocusValidation(string root, RimeEngine engine, bool x86 = false)
    {
        this.root = root; this.engine = engine; this.x86 = x86; ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        Shown += async (_, _) =>
        {
            string? error = null;
            try { await Run(); Result = 0; }
            catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
            finally
            {
                if (active != null) { await active.ShutdownAsync(); active.Dispose(); }
                overlay?.Dispose();
                foreach (var editor in editors) { if (!editor.HasExited) editor.CloseMainWindow(); editor.Dispose(); }
                var json = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = Result == 0, checks, error }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(root, "artifacts", x86 ? "focus-x86-verification.json" : "focus-verification.json"), json); Console.WriteLine(json); Close();
            }
        };
    }
    private void Check(bool result, string message) { if (!result) throw new Exception(message + "\n" + active?.Message); checks.Add(message); }
    private static string Read(AutomationElement field) => ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
    private async Task<AutomationElement> Launch(bool readOnly = false)
    {
        var start = new ProcessStartInfo(Path.Combine(root, "artifacts", x86 ? "test-editor-installed-x86" : "test-editor-installed", "TestEditor.exe")) { UseShellExecute = false };
        start.ArgumentList.Add(Path.Combine(root, "artifacts", "focus-editor-" + Guid.NewGuid().ToString("N")));
        if (readOnly) start.ArgumentList.Add("--read-only");
        var process = Process.Start(start)!; editors.Add(process);
        for (var i = 0; i < 80; i++)
        {
            await Task.Delay(50); process.Refresh(); if (process.MainWindowHandle == 0) continue;
            var window = AutomationElement.FromHandle(process.MainWindowHandle);
            var field = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            if (field != null) return field;
        }
        throw new Exception("Owned editor missing");
    }
    private async Task Focus(AutomationElement field)
    {
        var pid = field.Current.ProcessId;
        var process = editors.Single(p => p.Id == pid);
        for (var i = 0; i < 15; i++)
        {
            process.Refresh(); SetForegroundWindow(process.MainWindowHandle); field.SetFocus(); await Task.Delay(80);
            if (InputMethodSwitcher.Foreground() is InputWindow window && window.Process == pid &&
                AutomationElement.FocusedElement.GetRuntimeId().SequenceEqual(field.GetRuntimeId()))
            { targetWindow = window; targetField = field; return; }
        }
        throw new Exception("Owned editor did not gain foreground focus; no other program will be queried or edited.");
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    private async Task Begin(InputBehavior behavior, bool sameSession = false)
    {
        if (!sameSession)
        {
            if (active != null) { await active.ShutdownAsync(); active.Dispose(); overlay?.Dispose(); }
            active = new(engine, new InputMethodSwitcher(root)) { ControlsReleased = () => released };
            overlay = new(active); active.Changed += () => overlay.Present(4, 1); active.OverlayBounds = () => overlay.Bounds;
        }
        var window = targetWindow;
        active!.FocusConfiguration = () => (behavior, window, "owned-test-editor");
        await active.Enable(true);
        if (behavior.Mode == InputFocusMode.None)
            Check(active.Enabled && overlay!.Visible && TsfClient.GetForegroundWindow() == window.Root, "Switching the same panel to no intervention preserves target focus");
        else
        {
            Check(active.Enabled && active.Focused?.OwnsFocus == true, $"{behavior.Mode}/{behavior.Completion}: input surface gains foreground focus");
            Check(behavior.Mode == InputFocusMode.External
                ? active.Focused!.Form.Visible && TsfClient.GetForegroundWindow() == active.Focused.Form.Handle
                : !active.Focused!.Form.Visible && overlay!.Visible && TsfClient.GetForegroundWindow() == overlay.Handle,
                $"{behavior.Mode}: only the intended input surface is shown and activated");
        }
    }
    private async Task Nihao()
    {
        foreach (var region in new[] { 5, 3, 3, 1, 5 }) await active!.Handle(new(PadAction.Region, region));
        Check(active!.View.Candidates.Length > 0 && Validation.Find(engine, "你好"), "Production Rime composition retains candidates while target is unfocused");
    }
    private void Snapshot(string name, Form form)
    {
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
        bitmap.Save(Path.Combine(root, "artifacts", name));
    }
    private async Task Run()
    {
        await VerifyProfiles(); VerifyController();
        var field = await Launch(); await Focus(field);
        var switcher = new InputMethodSwitcher(root);
        var before = (await switcher.RunAsync(targetWindow, "query")).Before;
        var inputEvents = NumericInput.SentKeyEvents;
        await Begin(new() { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard });
        await Nihao(); await active!.Handle(new(PadAction.Confirm));
        Check(active.Focused!.Draft == "你好" && Read(field) == "", "External candidate confirmation edits only the local textbox");
        await active.Handle(new(PadAction.SwitchMode));
        await active.Handle(new(PadAction.Region, 0)); await active.Handle(new(PadAction.Region, 4, true));
        Check(active.Focused.Draft == "你好10" && NumericInput.SentKeyEvents == inputEvents, "External numeric mode edits locally without OS keyboard events");
        active.Focused.Form.Editor.SelectedText = "😀"; await active.Handle(new(PadAction.Backspace));
        Check(active.Focused.Draft == "你好10", "External backspace removes a complete surrogate-pair character");
        await Task.Delay(150); Snapshot("external-input-window.png", active.Focused.Form);
        // Materialize the old clipboard before changing ownership; a live OLE proxy may
        // no longer be usable after SetText replaces its owner.
        var oldClipboard = Clipboard.GetDataObject();
        var clipboardSnapshot = new DataObject();
        if (oldClipboard != null)
            foreach (var format in oldClipboard.GetFormats(false))
            {
                var data = oldClipboard.GetData(format, false);
                if (data is MemoryStream stream) data = new MemoryStream(stream.ToArray());
                if (data != null) clipboardSnapshot.SetData(format, false, data);
            }
        var testClipboard = "你好10";
        try
        {
            released = false;
            var completion = active.Handle(new(PadAction.Complete)); await Task.Delay(120);
            Check(!completion.IsCompleted && active.Focused.OwnsFocus, "Completion waits for held controls before returning focus");
            released = true; await completion;
            Check(!active.Enabled && Clipboard.GetText() == testClipboard && Read(field) == "", "Clipboard completion closes input and copies exactly once without filling the target");
        }
        finally
        {
            released = true;
            if (Clipboard.ContainsText() && Clipboard.GetText() == testClipboard)
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { if (oldClipboard == null) Clipboard.Clear(); else Clipboard.SetDataObject(clipboardSnapshot, true, 0, 0); break; }
                    catch (ExternalException) when (attempt < 10) { await Task.Delay(80); }
                }
            }
        }
        Check(InputMethodSwitcher.Foreground() == targetWindow, "Completion returns focus to the original editor");
        Check((await switcher.RunAsync(targetWindow, "query")).After == before, "Clipboard-only mode preserves the original input method");

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External, Completion = CompletionDestination.Target });
        await Nihao(); await active!.Handle(new(PadAction.Complete));
        Check(!active.Enabled && Read(field) == "你好", "Long completion confirms the pending candidate and fills the target through TSF");
        Check((await switcher.RunAsync(targetWindow, "query")).After == before, "Target completion restores the original input method");

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "保留草稿"; await active.Enable(false);
        await Focus(field); await Begin(new() { Mode = InputFocusMode.External }, sameSession: true);
        Check(active.Focused!.Draft == "保留草稿", "Closing and reopening external input retains the draft for that program");
        active.Focused.Form.Editor.Text = new string('字', 1025); await active.Handle(new(PadAction.Complete));
        Check(active.Enabled && active.Focused.Draft.Length == 1025 && Read(field) == "你好", "Oversize TSF submissions retain the draft without partial insertion");
        active.Focused.Form.Editor.Text = "焦点保护";
        await Focus(field); await active.Handle(new(PadAction.Complete));
        Check(active.Enabled && Read(field) == "你好" && active.Focused.Draft == "焦点保护", "User focus changes stop automatic completion without stealing focus");
        await active.ShutdownAsync();

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "取消待提交"; released = false;
        var pending = active.Handle(new(PadAction.Complete)); await Task.Delay(80);
        await active.ShutdownAsync(); await pending; released = true;
        Check(!active.Enabled && active.Focused.Draft == "取消待提交" && Read(field) == "你好", "Shutdown cancels a pending release wait without submitting or discarding the draft");

        await Focus(field); await Begin(new() { Mode = InputFocusMode.Defocus });
        await Nihao(); released = false;
        Snapshot("defocus-input-panel.png", overlay!);
        var relay = active!.Handle(new(PadAction.Confirm)); await Task.Delay(120);
        Check(!relay.IsCompleted && Read(field) == "你好", "Defocus mode waits for neutral controls before forwarding a candidate");
        released = true; await relay;
        Check(active.Enabled && active.Focused!.OwnsFocus && Read(field) == "你好你好", "Defocus mode fills one candidate then returns focus to the panel");
        await active.Handle(new(PadAction.SwitchMode));
        await active.Handle(new(PadAction.Region, 2)); await Task.Delay(120);
        Check(Read(field) == "你好你好3" && active.Focused!.OwnsFocus, "Defocus numeric mode targets the original editor before returning focus");
        await active.Handle(new(PadAction.Backspace));
        Check(Read(field) == "你好你好" && active.Focused!.OwnsFocus, "Defocus backspace edits the target and returns focus");
        Check(!active.Focused.Form.Visible && TsfClient.GetForegroundWindow() == overlay!.Handle, "Candidate, numeric and backspace relays return to the grid without showing an input window");
        await active.Enable(false);
        Check((await switcher.RunAsync(targetWindow, "query")).After == before, "Closing defocus mode restores the original input method");
        await Focus(field); await Begin(new() { Mode = InputFocusMode.None }, sameSession: true);
        await active.Enable(false);
        await Focus(field); await Begin(new() { Mode = InputFocusMode.External }, sameSession: true);
        await active.Enable(false);

        var lostTarget = await Launch(); await Focus(lostTarget); await Begin(new() { Mode = InputFocusMode.Defocus });
        await Nihao();
        var lostProcess = editors.Single(p => p.Id == lostTarget.Current.ProcessId);
        lostProcess.CloseMainWindow(); await lostProcess.WaitForExitAsync();
        await active!.Handle(new(PadAction.Confirm));
        Check(active.Focused!.HasRetainedText && !active.Focused.Form.Visible && overlay!.Visible,
            "Failed defocus submission retains text without opening an input window");
        active.Focused.ClearRetainedText();
        Check(!active.Focused.HasRetainedText, "Panel recovery action explicitly clears retained defocus text");
        await active.Enable(false);

        var readOnly = await Launch(true); await Focus(readOnly); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "不能丢失"; await active.Handle(new(PadAction.Complete));
        Check(active.Enabled && active.Focused.Draft == "不能丢失" && Read(readOnly) == "", "Read-only target failure preserves the draft and keeps input open");
        await active.ShutdownAsync();

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "窗口已关闭";
        editors[0].CloseMainWindow(); await editors[0].WaitForExitAsync();
        await active.Handle(new(PadAction.Complete));
        Check(active.Enabled && active.Focused.Draft == "窗口已关闭", "Destroyed target never receives text and draft remains available");
    }
    private async Task VerifyProfiles()
    {
        var folder = Path.Combine(root, "artifacts", "profiles-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        try
        {
            var defaults = ProgramProfiles.Load(folder, out var warning);
            Check(warning == null && defaults.Resolve("C:\\Game.exe").Mode == InputFocusMode.None, "Missing program settings default to no focus intervention");
            using var form = new ProgramProfilesForm(defaults, p => p.Save(folder)); form.TopMost = true; form.Show(); await Task.Delay(200);
            ComboBox Selector(string name) => (ComboBox)form.Controls.Find(name, true).Single();
            var mode = Selector("InputModeSelector"); var destination = Selector("CompletionSelector");
            mode.SelectedIndex = (int)InputFocusMode.External; destination.SelectedIndex = (int)CompletionDestination.Clipboard;
            Check(mode.DropDownStyle == ComboBoxStyle.DropDownList && destination.DropDownStyle == ComboBoxStyle.DropDownList && mode.Items.Count == 3 && destination.Items.Count == 2 && destination.Enabled,
                "Mode and completion use single-selection dropdowns with three and two options");
            form.AddProgram("C:\\Games\\Example.exe"); mode.SelectedIndex = (int)InputFocusMode.Defocus;
            Check(!destination.Enabled, "Completion dropdown is disabled outside external input mode");
            var draft = form.Draft; draft.Save(folder); var loaded = ProgramProfiles.Load(folder, out warning);
            Check(warning == null && loaded.Resolve("c:\\games\\EXAMPLE.exe").Mode == InputFocusMode.Defocus && loaded.Resolve("C:\\Other.exe") == new InputBehavior { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard }, "Per-executable overrides and global fallback survive save/reload with case-insensitive paths");
            ((ListBox)form.Controls.Find("ProgramList", true).Single()).SelectedIndex = 0;
            await Task.Delay(150);
            Snapshot("program-profiles-window.png", form);
            ((Button)form.Controls.Find("CancelProfiles", true).Single()).PerformClick();
            Check(ProgramProfiles.Load(folder, out _).Global == loaded.Global, "Canceling program configuration does not save edits");
            File.WriteAllText(Path.Combine(folder, "program-profiles.json"), "{\"Global\":{\"Mode\":999}}");
            Check(ProgramProfiles.Load(folder, out warning).Global.Mode == InputFocusMode.None && warning != null, "Malformed profile values safely fall back with a warning");
        }
        finally { foreach (var file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
    }
    private void VerifyController()
    {
        var pad = new Controller(); pad.ConfigureCompletion(true); pad.Update(default, 0);
        Check(pad.Update(new() { Buttons = Buttons.A }, 1).Count == 0 && pad.Update(default, 40).Single().Action == PadAction.Confirm, "External short A confirms on release");
        pad.Update(new() { Buttons = Buttons.A }, 100);
        Check(pad.Update(new() { Buttons = Buttons.A }, 1100).Single().Action == PadAction.Complete && pad.Update(new() { Buttons = Buttons.A }, 1300).Count == 0 && pad.Update(default, 1400).Count == 0, "Long A completes once and never produces a trailing short confirmation");
        pad.Update(new() { Buttons = Buttons.A }, 1410); pad.Update(new() { Buttons = Buttons.A | Buttons.B }, 1420);
        pad.Update(new() { Buttons = Buttons.A }, 1430);
        Check(pad.Update(default, 1440).Count == 0, "Canceling while A is held prevents a later release from confirming a candidate");
        pad.ConfigureCompletion(false); pad.Update(default, 1500);
        Check(pad.Update(new() { Buttons = Buttons.A }, 1600).Single().Action == PadAction.Confirm, "Existing direct mode still confirms A on press");
    }
}
