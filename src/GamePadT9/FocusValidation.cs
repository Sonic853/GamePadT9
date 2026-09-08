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
    private readonly Dictionary<int, string> profileReports = [];
    private InputSession? active;
    private MainForm? overlay;
    private bool released = true;
    private Gamepad heldState;
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
        var report = Path.Combine(root, "artifacts", "focus-editor-" + Guid.NewGuid().ToString("N"));
        start.ArgumentList.Add(report); start.ArgumentList.Add("--report-profile");
        if (readOnly) start.ArgumentList.Add("--read-only");
        var process = Process.Start(start)!; editors.Add(process); profileReports.Add(process.Id, report + ".profile.json");
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
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int count, [Out] nint[]? layouts);
    private async Task ExpectProfile(AutomationElement field, InputProfile profile, string description)
    {
        var report = profileReports[field.Current.ProcessId];
        InputProfile? actual = null;
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(80);
            if (!File.Exists(report)) continue;
            using var stream = new FileStream(report, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            actual = JsonSerializer.Deserialize<InputProfile>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (actual == profile) { Check(true, description); return; }
        }
        throw new Exception(description + ": expected " + JsonSerializer.Serialize(profile) + ", actual " + JsonSerializer.Serialize(actual));
    }
    private async Task Begin(InputBehavior behavior, bool sameSession = false)
    {
        if (!sameSession)
        {
            if (active != null) { await active.ShutdownAsync(); active.Dispose(); overlay?.Dispose(); }
            active = new(engine, new InputMethodSwitcher(root))
            {
                ControlsReleased = () => released && Controller.IsNeutral(heldState),
                ButtonsReleased = () => released && Controller.AreButtonsReleased(heldState)
            };
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
        await VerifyProfileRestoration();
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
            heldState = new() { RX = 22000 }; released = true;
            await Task.Delay(120);
            Check(!completion.IsCompleted && active.Focused.OwnsFocus, "External completion still waits for sticks to center");
            heldState = default; await completion;
            Check(!active.Enabled && Clipboard.GetText() == testClipboard && Read(field) == "", "Clipboard completion closes input and copies exactly once without filling the target");
        }
        finally
        {
            released = true; heldState = default;
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
        await Nihao(); heldState = new() { RX = 22000, RY = 22000, Buttons = Buttons.A };
        Snapshot("defocus-input-panel.png", overlay!);
        var relay = active!.Handle(new(PadAction.Confirm)); await Task.Delay(120);
        Check(!relay.IsCompleted && Read(field) == "你好", "Defocus confirmation waits for the confirm button to be released");
        heldState.Buttons = 0; await relay.WaitAsync(TimeSpan.FromSeconds(4));
        Check(active.Enabled && active.Focused!.OwnsFocus && Read(field) == "你好你好", "Defocus mode fills the candidate with the right stick still tilted, then returns focus to the panel");
        await active.Handle(new(PadAction.SwitchMode));
        heldState = new() { LX = -22000, LY = 22000, RX = 22000, RY = 22000, RT = 200 };
        var number = active.Handle(new(PadAction.Region, 2)); await Task.Delay(120);
        Check(!number.IsCompleted && Read(field) == "你好你好", "Defocus numeric input still waits for trigger release");
        heldState.RT = 0; await number.WaitAsync(TimeSpan.FromSeconds(4));
        Check(Read(field) == "你好你好3" && active.Focused!.OwnsFocus, "Defocus numeric input succeeds with both sticks tilted and returns focus");
        heldState.Buttons = Buttons.R3;
        var zero = active.Handle(new(PadAction.Region, 4, true)); await Task.Delay(120);
        Check(!zero.IsCompleted && Read(field) == "你好你好3", "Numeric zero waits for the stick-click button release");
        heldState.Buttons = 0; await zero.WaitAsync(TimeSpan.FromSeconds(4));
        Check(Read(field) == "你好你好30", "Numeric zero succeeds without centering the clicked stick");
        heldState = default;
        await active.Handle(new(PadAction.Backspace));
        await active.Handle(new(PadAction.Backspace));
        Check(Read(field) == "你好你好" && active.Focused!.OwnsFocus, "Defocus backspace edits the target and returns focus");
        Check(!active.Focused.Form.Visible && TsfClient.GetForegroundWindow() == overlay!.Handle, "Candidate, numeric and backspace relays return to the grid without showing an input window");
        heldState = new() { LX = 22000 };
        var closing = active.Enable(false); await Task.Delay(120);
        Check(!closing.IsCompleted && active.Focused.OwnsFocus, "Closing defocus input still waits for sticks to center");
        heldState = default; await closing;
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
        var closedEditor = editors.Single(p => p.Id == field.Current.ProcessId);
        closedEditor.CloseMainWindow(); await closedEditor.WaitForExitAsync();
        await active.Handle(new(PadAction.Complete));
        Check(active.Enabled && active.Focused.Draft == "窗口已关闭", "Destroyed target never receives text and draft remains available");
    }
    private async Task VerifyProfileRestoration()
    {
        var field = await Launch(); await Focus(field);
        var control = new InputMethodSwitcher(root);
        var initial = (await control.RunAsync(targetWindow, "query")).Before;
        var layouts = new nint[GetKeyboardLayoutList(0, null)]; GetKeyboardLayoutList(layouts.Length, layouts);
        var english = new InputProfile(2, 0x0409, Guid.Empty, Guid.Empty, layouts.First(h => ((long)h & 0xffff) == 0x0409).ToString("X"));
        try
        {
            foreach (var mode in new[] { InputFocusMode.Defocus, InputFocusMode.External })
            {
                await Focus(field);
                Check((await control.RunAsync(targetWindow, "restore", english)).Ok, "Prepare a different original keyboard layout for " + mode);
                await ExpectProfile(field, english, "Target independently reports the original English layout");
                await Begin(new() { Mode = mode });
                await Nihao();
                await active!.Handle(new(mode == InputFocusMode.External ? PadAction.Complete : PadAction.Confirm));
                if (active.Enabled) await active.Handle(new(PadAction.Disable));
                Check(InputMethodSwitcher.Foreground() == targetWindow, mode + ": close returns the original target to foreground");
                await Task.Delay(250);
                await ExpectProfile(field, english, mode + ": closing restores English after focus returns, using independent TSF readback");
            }

            // Focus transitions or the target can change its profile while the local draft is open.
            // Closing must restore the snapshot from activation, not this later profile.
            await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
            Check((await control.RunAsync(targetWindow, "restore", initial)).Ok, "Simulate a target profile change while editing externally");
            await Nihao(); await active!.Handle(new(PadAction.Complete));
            await ExpectProfile(field, english, "External completion restores the activation-time snapshot, not the submission-time profile");

            foreach (var mode in new[] { InputFocusMode.Defocus, InputFocusMode.External })
            foreach (var close in new[] { "toggle", "disable", "shutdown" })
            {
                await Focus(field); await Begin(new() { Mode = mode });
                if (mode == InputFocusMode.External) active!.Focused!.Form.Editor.Text = "保留草稿";
                if (close == "shutdown") { released = false; await active!.ShutdownAsync(); released = true; }
                else await active!.Handle(new(close == "toggle" ? PadAction.Toggle : PadAction.Disable));
                Check(!active.Enabled && !overlay!.Visible && InputMethodSwitcher.Foreground() == targetWindow,
                    mode + "/" + close + ": closes the panel and returns the original target");
                await ExpectProfile(field, english, mode + "/" + close + ": restores the original input method after returning focus");
                if (mode == InputFocusMode.External) Check(active.Focused!.Draft == "保留草稿", "Canceling external input preserves its draft");
            }
        }
        finally
        {
            released = true;
            if (active != null) await active.ShutdownAsync();
            if (targetWindow.IsAlive) await control.RunAsync(targetWindow, "restore", initial);
        }
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
