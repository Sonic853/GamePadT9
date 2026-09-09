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
                Preferences = new() { PanelBlur = 25 },
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
                ? active.Focused!.Form.IsVisible && TsfClient.GetForegroundWindow() == active.Focused.Form.Handle
                : !active.Focused!.Form.IsVisible && overlay!.Visible && TsfClient.GetForegroundWindow() == overlay.Handle,
                $"{behavior.Mode}: only the intended input surface is shown and activated");
        }
    }
    private async Task Nihao()
    {
        foreach (var region in new[] { 5, 3, 3, 1, 5 }) await active!.Handle(new(PadAction.Region, region));
        Check(active!.View.Candidates.Length > 0 && Validation.Find(engine, "你好"), "Production Rime composition retains candidates while target is unfocused");
    }
    private async Task SetMode(InputMode mode)
    {
        for (var i = 0; i < 3 && active!.Mode != mode; i++) await active.Handle(new(PadAction.SwitchMode));
        if (active!.Mode != mode) throw new Exception("Could not switch to " + mode);
    }
    private void Snapshot(string name, Form form)
    {
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(form.PointToScreen(Point.Empty), Point.Empty, bitmap.Size);
        bitmap.Save(Path.Combine(root, "artifacts", name));
    }
    private async Task VerifyExternalWindow()
    {
        var window = active!.Focused!.Form;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ContextIdle);
        await Task.Delay(150);
        Check(window.Editor.ActualHeight is >= 30 and <= 36 && window.CompleteButton.ActualHeight is >= 26 and <= 32 && window.ActualHeight <= 114,
            "External Fluent editor stays one line high with compact actions and no title row");
        Check(window.TargetProgram.Text == "TestEditor.exe" && ProgramProfiles.Executable(targetWindow)?.EndsWith("TestEditor.exe", StringComparison.OrdinalIgnoreCase) == true,
            "The x86 host identifies the owned target executable and displays it in the external window");
        Check(window.DestinationLabel.Text.Contains("剪贴板") && window.CompletionLabel.Text == "完成并复制",
            "External window displays the configured completion destination");
        Check(GetWindowRect(window.Handle, out var bounds) && bounds.Bottom + 4 <= overlay!.Top,
            "Fluent window respects its compact height and does not overlap the nine-grid panel");
        var area = Screen.FromRectangle(overlay!.Bounds).WorkingArea;
        Check(bounds.Left >= area.Left && bounds.Top >= area.Top && bounds.Right <= area.Right,
            "External window fits within the current monitor work area");
        window.PlaceAbove(new Rectangle(overlay.Right - 390, overlay.Top, 390, overlay.Height));
        window.UpdateLayout(); await Task.Delay(100);
        var cancelRight = window.CancelButton.PointToScreen(new System.Windows.Point(window.CancelButton.ActualWidth, 0)).X;
        var completeLeft = window.CompleteButton.PointToScreen(new System.Windows.Point()).X;
        Check(cancelRight + 4 <= completeLeft && window.Editor.ActualHeight is >= 30 and <= 36 &&
            Math.Abs(window.TargetProgram.PointToScreen(new()).Y - window.Status.PointToScreen(new()).Y) <= 1 &&
            Math.Abs(window.DestinationLabel.PointToScreen(new()).Y - window.CharacterCount.PointToScreen(new()).Y) <= 1,
            "Compact placement keeps actions separate and program, destination, status and count on one row");
        foreach (var family in Enum.GetValues<GamepadFamily>()) window.UseFamily(family);
        Check(((SteamGlyph)window.CompletionGlyph.Content).Family == Enum.GetValues<GamepadFamily>().Last(),
            "Completion uses the active controller family's Steam SVG glyph");
        window.UseFamily(GamepadFamily.Xbox); window.PlaceAbove(overlay.Bounds);

        window.Editor.Text = "甲乙丙"; window.Editor.Select(1, 1);
        await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9);
        await active.Handle(new(PadAction.Region, 0)); await active.Handle(new(PadAction.Region, 1));
        Check(window.Editor.Text == "甲12丙" && window.Editor.SelectionStart == 3 && window.Editor.SelectionLength == 0,
            "WPF gamepad commits replace the selection once and then insert at the advancing caret");
        await active.Handle(new(PadAction.Backspace)); window.Editor.Select(0, 2); await active.Handle(new(PadAction.Backspace));
        Check(window.Editor.Text == "丙", "External backspace removes both preceding text and an explicit selection correctly");
        window.Editor.Text = "好😀e\u0301"; window.Editor.CaretIndex = window.Editor.Text.Length;
        await active.Handle(new(PadAction.Backspace));
        Check(window.Editor.Text == "好😀", "External backspace removes a combining character as one text element");
        await active.Handle(new(PadAction.Backspace));
        Check(window.Editor.Text == "好", "External backspace at the caret removes a whole emoji");
        window.Editor.Text = "甲乙"; window.Editor.CaretIndex = 2; window.Editor.Focus();
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.ContextIdle);
        await Task.Delay(80); // Let TSF and WPF finish the preceding read-only/focus changes before native key delivery.
        Check(active.Focused.OwnsFocus && window.Editor.IsKeyboardFocused, "External WPF editor receives native keyboard focus under the tray message loop");
        SendKeys.SendWait("{LEFT}"); await Task.Delay(80);
        SendKeys.SendWait("{ENTER}"); await Task.Delay(80);
        Check(window.Editor.Text.Replace("\r", "") == "甲\n乙", "Native arrow and Enter keys edit multiple lines without triggering completion: " +
            JsonSerializer.Serialize(window.Editor.Text) + ", caret=" + window.Editor.CaretIndex + ", focused=" + window.Editor.IsKeyboardFocused);
        window.Click("ClearButton");
        Check(window.Editor.Text == "" && active.Focused.Draft == "" && window.Editor.IsKeyboardFocused,
            "The Fluent clear action clears the draft and returns the caret to the editor");
        await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9);
    }
    [StructLayout(LayoutKind.Sequential)] private struct WindowBounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out WindowBounds bounds);
    private async Task Run()
    {
        await VerifyEnglish();
        await VerifyLayouts();
        await VerifySpaces();
        await VerifyMixedInput();
        await VerifyProfiles(); VerifyController();
        await VerifyProfileRestoration();
        var field = await Launch(); await Focus(field);
        var switcher = new InputMethodSwitcher(root);
        var before = (await switcher.RunAsync(targetWindow, "query")).Before;
        var inputEvents = NumericInput.SentKeyEvents;
        await Begin(new() { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard });
        await VerifyExternalWindow();
        await Nihao(); await active!.Handle(new(PadAction.Confirm));
        Check(active.Focused!.Draft == "你好" && Read(field) == "", "External candidate confirmation edits only the local textbox");
        await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9);
        await active.Handle(new(PadAction.Region, 0)); await active.Handle(new(PadAction.Region, 4, true));
        Check(active.Focused.Draft == "你好10" && NumericInput.SentKeyEvents == inputEvents, "External numeric mode edits locally without OS keyboard events");
        active.Focused.Form.Editor.SelectedText = "😀"; await active.Handle(new(PadAction.Backspace));
        Check(active.Focused.Draft == "你好10", "External backspace removes a complete surrogate-pair character");
        await Task.Delay(250); active.Focused.Form.UpdateLayout();
        PanelSnapshot.Save(active.Focused.Form, Path.Combine(root, "artifacts", "external-input-window.png"));
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
            active.Focused.Form.Click("CopyButton");
            for (var i = 0; i < 40 && (!Clipboard.ContainsText() || Clipboard.GetText() != testClipboard); i++) await Task.Delay(25);
            Check(active.Enabled && active.Focused.Draft == testClipboard && Clipboard.GetText() == testClipboard,
                "The Fluent copy action copies the draft without closing input or discarding text");
            var chord = new Controller(); chord.ConfigureCompletion(true); chord.Update(default, 0);
            heldState = new() { Buttons = Buttons.View | Buttons.Menu };
            released = false;
            var completion = active.Handle(chord.Update(heldState, 10).Single()); await Task.Delay(120);
            Check(!completion.IsCompleted && active.Focused.OwnsFocus && chord.Update(heldState, 130).Count == 0,
                "View + Menu completes the external draft once and waits for the held chord before returning focus");
            await active.Handle(new(PadAction.Toggle));
            Check(!completion.IsCompleted && active.Enabled, "Repeated View + Menu while completing does not cancel the in-flight clipboard operation");
            Check(active.Focused.Form.Editor.IsReadOnly && !active.Focused.Form.CompleteButton.IsEnabled && !active.Focused.Form.ClearButton.IsEnabled && !active.Focused.Form.CopyButton.IsEnabled,
                "External submission locks editing and duplicate actions while waiting for controls to release");
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
        await Nihao();
        active!.Focused!.Form.UseFamily(GamepadFamily.PS5);
        await Task.Delay(150);
        PanelSnapshot.Save(active.Focused.Form, Path.Combine(root, "artifacts", "external-input-ps5-window.png"));
        active.Focused.Form.Click("CompleteButton");
        for (var i = 0; i < 160 && (active.Enabled || active.Busy); i++) await Task.Delay(25);
        Check(!active.Enabled && !active.Busy && Read(field) == "你好", "The Fluent completion button confirms the pending candidate and fills the target through TSF");
        Check((await switcher.RunAsync(targetWindow, "query")).After == before, "Target completion restores the original input method");

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "保留草稿"; await active.Enable(false);
        await Focus(field); await Begin(new() { Mode = InputFocusMode.External }, sameSession: true);
        Check(active.Focused!.Draft == "保留草稿", "Closing and reopening external input retains the draft for that program");
        active.Focused.Form.Close();
        for (var i = 0; i < 160 && (active.Enabled || active.Busy); i++) await Task.Delay(25);
        Check(!active.Enabled && !active.Busy && !active.Focused.Form.IsVisible && active.Focused.Draft == "保留草稿" && InputMethodSwitcher.Foreground() == targetWindow,
            "Closing the compact Fluent window follows the normal focus restoration path and retains the draft");
        await Focus(field); await Begin(new() { Mode = InputFocusMode.External }, sameSession: true);
        active.Focused.Form.Editor.Text = new string('字', 1025); await active.Handle(new(PadAction.Toggle));
        Check(active.Enabled && active.Focused.Draft.Length == 1025 && Read(field) == "你好", "Oversize TSF submissions retain the draft without partial insertion");
        active.Focused.Form.Editor.Text = "焦点保护";
        await Focus(field); await active.Handle(new(PadAction.Toggle));
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
        await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9);
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
        Check(!active.Focused.Form.IsVisible && TsfClient.GetForegroundWindow() == overlay!.Handle, "Candidate, numeric and backspace relays return to the grid without showing an input window");
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
        Check(active.Focused!.HasRetainedText && !active.Focused.Form.IsVisible && overlay!.Visible,
            "Failed defocus submission retains text without opening an input window");
        active.Focused.ClearRetainedText();
        Check(!active.Focused.HasRetainedText, "Panel recovery action explicitly clears retained defocus text");
        await active.Enable(false);

        var readOnly = await Launch(true); await Focus(readOnly); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "不能丢失"; await active.Handle(new(PadAction.Toggle));
        Check(active.Enabled && active.Focused.Draft == "不能丢失" && Read(readOnly) == "", "Read-only target failure preserves the draft and keeps input open");
        await active.ShutdownAsync();

        await Focus(field); await Begin(new() { Mode = InputFocusMode.External });
        active!.Focused!.Form.Editor.Text = "窗口已关闭";
        var closedEditor = editors.Single(p => p.Id == field.Current.ProcessId);
        closedEditor.CloseMainWindow(); await closedEditor.WaitForExitAsync();
        await active.Handle(new(PadAction.Toggle));
        Check(active.Enabled && active.Focused.Draft == "窗口已关闭", "Destroyed target never receives text and draft remains available");
    }
    private async Task VerifyEnglish()
    {
        var field = await Launch();
        foreach (var behavior in new[] { InputFocusMode.None, InputFocusMode.Defocus, InputFocusMode.External })
        {
            await Focus(field); ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
            await Begin(new() { Mode = behavior, Completion = CompletionDestination.Target });
            var beforeEvents = NumericInput.SentKeyEvents;
            var pad = new Controller(active!.English);
            void Sync()
            {
                pad.ConfigureCompletion(active.Enabled && active.ExternalInput);
                pad.ConfigureEnglish(active.Enabled && active.Mode == InputMode.English);
                overlay!.Present(pad.Region, 1, pad.Detail);
            }
            active.Changed += Sync; Sync(); pad.Update(default, 0);
            long time = 10;
            string Current() => behavior == InputFocusMode.External ? active.Focused!.Draft : Read(field);
            async Task Sample(Gamepad state)
            {
                heldState = state;
                foreach (var action in pad.Update(state, time++))
                {
                    var task = active.Handle(action);
                    if (!task.IsCompleted)
                    {
                        // Like the host, keep polling button release while a
                        // defocus relay awaits it, without centering the sticks.
                        await Task.Delay(70);
                        heldState.Buttons = 0; heldState.LT = heldState.RT = 0;
                        pad.Update(heldState, time++);
                    }
                    await task.WaitAsync(TimeSpan.FromSeconds(6));
                }
                overlay!.Present(pad.Region, 1, pad.Detail);
            }
            async Task Press(Gamepad state, Buttons button = 0)
            {
                await Sample(state);
                var pressed = state;
                if (button == 0) pressed.RT = 200; else pressed.Buttons = button;
                await Sample(pressed); await Sample(state);
            }
            try
            {
                await Press(default, Buttons.Y); Check(active.Mode == InputMode.English, behavior + ": Y cycles from Chinese to English");
                await Press(default, Buttons.Y); Check(active.Mode == InputMode.Numeric, behavior + ": Y cycles from English to numeric");
                await Press(default, Buttons.Y); Check(active.Mode == InputMode.T9, behavior + ": Y cycles back to Chinese");
                await Press(default, Buttons.Y);
                await active.Handle(new(PadAction.Region, 5));
                await Press(default, Buttons.RB);
                Check(active.English.Uppercase && active.English.Group == 5, behavior + ": the default right shoulder switches to uppercase without leaving the group");
                await active.Handle(new(PadAction.Region, 5, Detail: 1));
                Check(Current() == "N", behavior + ": uppercase reaches the text target without prediction");
                await Sample(new() { Buttons = Buttons.RB }); time += 600;
                await Sample(new() { Buttons = Buttons.RB }); time += 600;
                await Sample(new() { Buttons = Buttons.RB }); await Sample(default);
                Check(!active.English.Uppercase, behavior + ": holding the right shoulder changes case once rather than on repeats");
                await Press(default, Buttons.LB);
                Check(!active.English.Uppercase, behavior + ": the unassigned left shoulder does not change English case");
                await Press(default, Buttons.RB);
                Check(active.English.Uppercase, behavior + ": the next right-shoulder click changes case again");
                if (behavior == InputFocusMode.External)
                { using var bitmap = overlay!.CreateSnapshot(); bitmap.Save(Path.Combine(root, "artifacts", "english-uppercase.png")); }
                await Press(default, Buttons.B);
                await Press(new() { RX = -22000, RY = 22000 });
                await Press(default, Buttons.RB);
                await Sample(new() { Buttons = Buttons.RB }); time += 600;
                await Sample(new() { Buttons = Buttons.RB }); await Sample(default);
                await Press(default, Buttons.LB);
                Check(active.View.Highlight == 2 && active.English.Uppercase,
                    behavior + ": both shoulders navigate symbols, including held repeats, without changing case");
                await Press(default, Buttons.A);
                await active.Handle(new(PadAction.Next, IsRepeat: true));
                Check(Current() == "N?" && active.English.Uppercase, behavior + ": closing symbols does not turn a held shoulder repeat into a case toggle");
                var oldPreferences = active.Preferences;
                active.Preferences = oldPreferences with { EnglishCaseShoulder = ControlSide.Left };
                await Press(default, Buttons.LB);
                Check(!active.English.Uppercase, behavior + ": assigning the left shoulder moves case switching to LB");
                await Press(default, Buttons.RB);
                Check(!active.English.Uppercase, behavior + ": RB stops switching case after assigning LB");
                await Press(default, Buttons.LB);
                await active.Handle(new(PadAction.Region, 1, Detail: 0));
                await Press(new() { RX = -22000, RY = 22000 });
                await Press(default, Buttons.RB); await Press(default, Buttons.LB);
                Check(active.View.Highlight == 0 && active.English.Uppercase,
                    behavior + ": the left-shoulder preference does not override symbol navigation");
                await Press(default, Buttons.A);
                Check(Current() == "N?A.", behavior + ": uppercase setting leaves ASCII punctuation unchanged");
                await SetMode(InputMode.Numeric); await Press(default, Buttons.LB);
                await SetMode(InputMode.T9); await Press(default, Buttons.RB);
                await SetMode(InputMode.English);
                Check(active.English.Uppercase, behavior + ": other modes neither toggle nor discard the chosen English case");
                await Press(default, Buttons.LB); active.Preferences = oldPreferences;
                for (var i = 0; i < 4; i++) await active.Handle(new(PadAction.Backspace));
                Check(Current() == "" && !active.English.Uppercase, behavior + ": lowercase is restored for normal English input");
                await Press(new() { RY = 22000 });
                Check(active.English.Group == 1 && Current() == "", behavior + ": selecting ABC opens four cells without inserting anything");
                await Sample(default);
                Check(overlay!.DetailsExpanded && overlay.HighlightedDetail == -1 && overlay.HighlightedRegion == 1,
                    behavior + ": centered English group remains expanded with no highlighted letter");
                if (behavior == InputFocusMode.External)
                { using var bitmap = overlay.CreateSnapshot(); bitmap.Save(Path.Combine(root, "artifacts", "english-centered.png")); }
                await Press(default);
                Check(active.English.Group == null && !overlay.DetailsExpanded && Current() == "", behavior + ": centered trigger returns to the nine-grid");
                await Press(new() { RY = 22000 }, Buttons.R3);
                await Press(new() { RX = 22000, RY = 22000 });
                Check(Current() == "a" && active.English.Group == 1, behavior + ": right stick inputs a literal and retains the selected group");
                await Press(new() { RX = 22000, RY = 22000, LX = 22000, LY = -22000 }, Buttons.R3);
                Check(Current() == "ab", behavior + ": left-stick B wins over right-stick A on a stick click");
                await Press(new() { RX = 22000, RY = 22000, LX = -22000, LY = 22000 });
                Check(Current() == "ab" && overlay.HighlightedDetail == 3, behavior + ": highlighted blank is a no-op even with a valid right-stick letter");
                if (behavior == InputFocusMode.External)
                { using var bitmap = overlay.CreateSnapshot(); bitmap.Save(Path.Combine(root, "artifacts", "english-blank.png")); }
                await Press(new() { RX = -22000, RY = 22000 });
                Check(Current() == "ab", behavior + ": a right-stick blank also inserts nothing");
                await Press(default, Buttons.B);
                Check(active.English.Group == null && active.Enabled && Current() == "ab", behavior + ": B leaves the four-cell view without closing input");
                await Press(new() { LX = -22000, LY = -22000 });
                Check(Current() == "abl" && active.English.Group == null, behavior + ": two-stick English directly inputs the selected letter without latching");
                await Press(default, Buttons.A); Check(Current() == "abl ", behavior + ": English A inserts one ASCII space");
                await active.Handle(new(PadAction.Backspace)); Check(Current() == "abl", behavior + ": English X deletes the preceding character");
                await Press(new() { RX = -22000, RY = 22000 }, Buttons.R3);
                Check(active.English.Group == null && active.View.Candidates[0].Text == "." && active.View.Candidates.Length == 9,
                    behavior + ": symbol click opens English-first common symbols immediately");
                await Press(default, Buttons.RB); await Press(default, Buttons.A);
                Check(Current() == "abl," && active.View.Candidates.Length == 0, behavior + ": English punctuation uses candidate navigation and literal submission");
                await Press(new() { RX = -22000, RY = 22000, LX = 22000, LY = -22000 });
                Check(Current() == "abl,.", behavior + ": symbol detail inserts an ASCII period directly");
                // The mouse route shares the same group state and letter resolver.
                await active.Handle(new(PadAction.Region, 6));
                await active.Handle(new(PadAction.Region, 6, Detail: 3));
                Check(Current() == "abl,.s" && active.English.Group == 6 && engine.View.Preedit.Length == 0 && engine.PendingCommit.Length == 0 && active.View.Candidates.Length == 0,
                    behavior + ": clicking a group then its letter types lowercase text without invoking Rime completion");
                await active.Handle(new(PadAction.Cancel));
                await SetMode(InputMode.T9); await Nihao(); var preedit = engine.View.Preedit;
                await SetMode(InputMode.English);
                await active.Handle(new(PadAction.Region, 1, Detail: 0)); await Press(default, Buttons.A);
                Check(Current() == "abl,.sa " && engine.View.Preedit == preedit && active.View.Candidates.Length == 0,
                    behavior + ": English letters and spaces preserve hidden Chinese composition");
                await active.Handle(new(PadAction.Region, 1)); await Press(default, Buttons.B);
                Check(engine.View.Preedit == preedit, behavior + ": leaving the English group with B does not discard hidden Chinese candidates");
                await SetMode(InputMode.T9); await active.Handle(new(PadAction.Confirm));
                Check(Current() == "abl,.sa 你好", behavior + ": switching back restores and commits the original Chinese candidate");
                await SetMode(InputMode.English); heldState = default;
                if (behavior == InputFocusMode.External) await Press(default, Buttons.View | Buttons.Menu);
                else await active.Enable(false);
                Check(!active.Enabled && Read(field) == "abl,.sa 你好" && NumericInput.SentKeyEvents == beforeEvents,
                    behavior + ": English completion reaches the target exactly once without simulated number keys");
            }
            finally { heldState = default; active.Changed -= Sync; await active.ShutdownAsync(); }
        }
        var process = editors.Single(p => p.Id == field.Current.ProcessId);
        process.CloseMainWindow(); await process.WaitForExitAsync();
    }
    private async Task VerifyLayouts()
    {
        var field = await Launch();
        int[] quadrants = [3, 0, 2, 1];
        foreach (var behavior in new[] { InputFocusMode.None, InputFocusMode.Defocus, InputFocusMode.External })
        {
            await Focus(field); ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
            await Begin(new() { Mode = behavior, Completion = CompletionDestination.Target });
            var beforeEvents = NumericInput.SentKeyEvents;
            string Current() => behavior == InputFocusMode.External ? active!.Focused!.Draft : Read(field);
            var expected = "";
            foreach (var layout in new[] { LetterLayout.Default, LetterLayout.Clockwise, LetterLayout.Rows })
            {
                active!.Preferences = active.Preferences with { PinyinLayout = layout, EnglishLayout = LetterLayout.Custom, EnglishCustomOrder = "2314" };
                foreach (var letter in "nihao")
                {
                    var group = Array.FindIndex(T9Layout.Groups, name => name.Contains(char.ToUpperInvariant(letter)));
                    var ordinal = T9Layout.Groups[group].IndexOf(char.ToUpperInvariant(letter));
                    var position = active.Preferences.PinyinOrder.IndexOf((char)('1' + ordinal));
                    await active.Handle(new(PadAction.Region, group, Detail: quadrants[position]));
                }
                Check(Validation.Find(engine, "你好"), behavior + ": physical quadrants compose nihao under preset " + layout);
                if (behavior == InputFocusMode.External)
                {
                    overlay!.Present(1, 1, 3); using var bitmap = overlay.CreateSnapshot();
                    bitmap.Save(Path.Combine(root, "artifacts", "layout-pinyin-" + layout + ".png"));
                }
                await active.Handle(new(PadAction.Confirm)); expected += "你好";
                Check(Current() == expected, behavior + ": reordered Chinese letters commit the correct word for " + layout);
            }
            active!.Preferences = active.Preferences with { PinyinLayout = LetterLayout.Custom, PinyinCustomOrder = "2341" };
            await active.Handle(new(PadAction.Region, 1, Detail: 2));
            Check(engine.View.Preedit.Length > 0, behavior + ": moved Chinese blank still falls back to the whole group");
            await active.Handle(new(PadAction.Cancel));
            await active.Handle(new(PadAction.Region, 0, Detail: 2)); expected += "，";
            Check(Current() == expected, behavior + ": Chinese comma stays at the lower left despite a custom blank at that position");
            await SetMode(InputMode.English);
            var pad = new Controller(active.English); pad.ConfigureEnglish(true); pad.Update(default, 0);
            pad.Update(new() { RY = 22000 }, 1);
            await active.Handle(pad.Update(new() { RY = 22000, RT = 200 }, 2).Single());
            Check(active.English.Group == 1, behavior + ": right-only English still locks the physical ABC group");
            pad.Update(new() { RX = 22000, RY = 22000 }, 3);
            await active.Handle(pad.Update(new() { RX = 22000, RY = 22000, RT = 200 }, 4).Single()); expected += "c";
            Check(Current() == expected, behavior + ": right-only English uses its independent custom upper-right letter");
            pad.Update(new() { RX = 22000, RY = 22000, LX = -22000, LY = -22000 }, 5);
            await active.Handle(pad.Update(new() { RX = 22000, RY = 22000, LX = -22000, LY = -22000, RT = 200 }, 6).Single()); expected += "a";
            Check(Current() == expected, behavior + ": left-stick priority resolves through the English custom order");
            await active.Handle(new(PadAction.Region, 1, Detail: 1));
            Check(Current() == expected && active.View.Candidates.Length == 0, behavior + ": moved English blank remains a no-op with no prediction");
            await active.Handle(new(PadAction.Next)); await active.Handle(new(PadAction.Region, 1, Detail: 3)); expected += "B";
            Check(Current() == expected, behavior + ": uppercase and mouse quadrant events use the same custom mapping");
            if (behavior == InputFocusMode.External)
            {
                overlay!.Present(1, 1, 3); using var bitmap = overlay.CreateSnapshot();
                bitmap.Save(Path.Combine(root, "artifacts", "layout-english-custom.png"));
            }
            active.Preferences = active.Preferences with { EnglishUsePinyinLayout = true };
            await active.Handle(new(PadAction.Region, 1, Detail: 2));
            Check(Current() == expected, behavior + ": inherited English blank follows the pinyin custom position");
            await active.Handle(new(PadAction.Region, 1, Detail: 1)); expected += "A";
            Check(Current() == expected, behavior + ": English reuse changes the submitted letter to the pinyin order");
            await active.Handle(new(PadAction.Region, 0, Detail: 2)); expected += ",";
            await active.Handle(new(PadAction.Region, 0, Detail: 3));
            Check(Current() == expected && active.View.Candidates[0].Text == ".", behavior + ": English comma and symbol-menu positions are never reordered");
            await active.Handle(new(PadAction.Cancel));
            if (behavior == InputFocusMode.External) await active.Handle(new(PadAction.Toggle)); else await active.Enable(false);
            Check(!active.Enabled && Read(field) == expected && NumericInput.SentKeyEvents == beforeEvents,
                behavior + ": all layout variants finish through text submission without simulated number keys");
        }
        var process = editors.Single(p => p.Id == field.Current.ProcessId);
        process.CloseMainWindow(); await process.WaitForExitAsync();
    }
    private async Task VerifySpaces()
    {
        var field = await Launch();
        foreach (var behavior in new[] { InputFocusMode.None, InputFocusMode.Defocus, InputFocusMode.External })
        {
            await Focus(field); ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
            await Begin(new() { Mode = behavior, Completion = CompletionDestination.Target });
            var beforeEvents = NumericInput.SentKeyEvents;
            var pad = new Controller(); pad.ConfigureCompletion(behavior == InputFocusMode.External); pad.Update(default, 0);
            long time = 10;
            string Current() => behavior == InputFocusMode.External ? active!.Focused!.Draft : Read(field);
            async Task ShortA()
            {
                var actions = pad.Update(new() { Buttons = Buttons.A }, time);
                Check(pad.Update(new() { Buttons = Buttons.A }, time + 100).Count == 0, behavior + ": holding A does not repeat selection or space");
                actions.AddRange(pad.Update(default, time + 150)); time += 200;
                foreach (var action in actions) await active!.Handle(action);
            }
            await ShortA(); Check(Current() == " ", behavior + ": empty T9 input inserts one ASCII space");
            await SetMode(active!.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9); await ShortA();
            Check(Current() == "  ", behavior + ": empty numeric input also inserts one space");
            await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9); await Nihao();
            var preedit = engine.View.Preedit;
            await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9); await ShortA();
            Check(Current() == "  " && engine.View.Preedit == preedit, behavior + ": hidden T9 composition in numeric mode prevents an accidental space");
            await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9); await ShortA();
            Check(Current() == "  你好", behavior + ": candidate selection never adds a trailing space");
            await active.Handle(new(PadAction.Region, 0)); await ShortA();
            Check(Current() == "  你好，", behavior + ": the symbol menu retains priority over inserting a space");
            await ShortA(); Check(Current() == "  你好， ", behavior + ": A inserts space after the last candidate is committed");
            if (behavior == InputFocusMode.External)
            {
                Check(pad.Update(new() { Buttons = Buttons.A }, time).Count == 0, "External long A does not insert a space on press");
                await active.Handle(pad.Update(new() { Buttons = Buttons.A }, time + 1000).Single());
                Check(pad.Update(default, time + 1100).Count == 0 && !active.Enabled && Read(field) == "  你好， ",
                    "External long A completes the draft without adding a space on release");
            }
            else await active.Enable(false);
            Check(!active.Enabled && Read(field) == "  你好， " && NumericInput.SentKeyEvents == beforeEvents,
                behavior + ": spaces reach the target as text without simulated keyboard events");
        }
        var process = editors.Single(p => p.Id == field.Current.ProcessId);
        process.CloseMainWindow(); await process.WaitForExitAsync();
    }
    private async Task VerifyMixedInput()
    {
        var field = await Launch();
        foreach (var behavior in new[] { InputFocusMode.None, InputFocusMode.Defocus, InputFocusMode.External })
        {
            await Focus(field);
            ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).SetValue("");
            await Begin(new() { Mode = behavior, Completion = CompletionDestination.Target });
            var beforeEvents = NumericInput.SentKeyEvents;
            // n + GHI + h + ABC(blank) + o mixes exact and ambiguous tokens.
            PadEvent[] code = [new(PadAction.Region, 5, Detail: 1), new(PadAction.Region, 3),
                new(PadAction.Region, 3, Detail: 1), new(PadAction.Region, 1, Detail: 3), new(PadAction.Region, 5, Detail: 2)];
            foreach (var action in code) await active!.Handle(action);
            Check(Validation.Find(engine, "你好"), behavior + ": mixed events including the blank preserve the expected candidate");
            if (behavior == InputFocusMode.External)
            {
                foreach (var (name, region, detail) in new[] { ("abc", 1, 0), ("blank", 1, 3), ("pqrs", 6, 3), ("symbols", 0, 2) })
                {
                    overlay!.Present(region, 1, detail);
                    using var image = overlay.CreateSnapshot();
                    image.Save(Path.Combine(root, "artifacts", "mixed-" + name + ".png"));
                    Check(overlay.HighlightedDetail == detail, "Visible detail selection survives candidate updates: " + name);
                }
                overlay!.Present(1, 1, -1); Check(overlay.HighlightedDetail == -1, "Centering restores the whole group view");
                await SetMode(active!.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9); overlay.Present(1, 1, 3);
                Check(overlay.HighlightedDetail == -1, "Numeric mode always displays whole digit cells");
                await SetMode(active.Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9);
            }
            await active!.Handle(new(PadAction.Region, 0, Detail: 2));
            await active.Handle(new(PadAction.Region, 0, Detail: 1));
            Check((behavior == InputFocusMode.External ? active.Focused!.Draft : Read(field)) == "你好，。",
                behavior + ": punctuation commits the mixed candidate and both Chinese marks through the production route");
            await active.Handle(new(PadAction.Region, 0, Detail: 3));
            Check(active.View.Candidates.Length > 0 && active.View.Candidates.Any(c => c.Text == "，"), behavior + ": upper symbol half opens the symbol menu");
            await active.Handle(new(PadAction.Cancel));
            if (behavior == InputFocusMode.External) await active.Handle(new(PadAction.Toggle)); else await active.Enable(false);
            Check(!active.Enabled && Read(field) == "你好，。" && NumericInput.SentKeyEvents == beforeEvents,
                behavior + ": mixed input finishes without synthetic keyboard events");
        }
        var process = editors.Single(p => p.Id == field.Current.ProcessId);
        process.CloseMainWindow(); await process.WaitForExitAsync();
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
                var beforeComposition = Read(field);
                await Begin(new() { Mode = mode });
                await Nihao();
                await active!.Handle(new(mode == InputFocusMode.External ? PadAction.Toggle : PadAction.Confirm));
                if (mode == InputFocusMode.External)
                    Check(!active.Enabled && Read(field) == beforeComposition + "你好" && active.Focused!.Draft == "",
                        "View + Menu confirms the pending external candidate, fills the target and clears the acknowledged draft");
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
                var previousText = Read(field);
                if (mode == InputFocusMode.External) active!.Focused!.Form.Editor.Text = "保留草稿";
                if (close == "shutdown") { released = false; await active!.ShutdownAsync(); released = true; }
                else if (mode == InputFocusMode.External && close == "toggle")
                {
                    heldState = new() { Buttons = Buttons.View | Buttons.Menu };
                    var completing = active!.Handle(new(PadAction.Toggle)); await Task.Delay(120);
                    Check(!completing.IsCompleted && active.Focused!.Draft == "保留草稿" && Read(field) == previousText,
                        "Target completion waits for View + Menu release before filling the original program");
                    await active.Handle(new(PadAction.Toggle));
                    Check(!completing.IsCompleted && active.Enabled, "A repeated completion chord does not abort or duplicate target submission");
                    heldState = default; await completing;
                }
                else await active!.Handle(new(close == "toggle" ? PadAction.Toggle : PadAction.Disable));
                Check(!active.Enabled && !overlay!.Visible && InputMethodSwitcher.Foreground() == targetWindow,
                    mode + "/" + close + ": closes the panel and returns the original target");
                await ExpectProfile(field, english, mode + "/" + close + ": restores the original input method after returning focus");
                if (mode == InputFocusMode.External)
                {
                    if (close == "toggle") Check(active.Focused!.Draft == "" && Read(field) == previousText + "保留草稿",
                        "View + Menu fills the target exactly once, closes input and clears the completed draft");
                    else Check(active.Focused!.Draft == "保留草稿" && Read(field) == previousText,
                        "Explicit cancellation preserves the external draft without filling the target");
                }
            }
        }
        finally
        {
            released = true; heldState = default;
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
            Check(warning == null && defaults.Resolve("C:\\Game.exe") == new InputBehavior { Mode = InputFocusMode.External, Completion = CompletionDestination.Target }, "Missing program settings default to external input with target completion");
            using var form = new ProgramProfilesForm(defaults, p => p.Save(folder)); form.Topmost = true; form.Show(); await Task.Delay(200);
            System.Windows.Controls.ComboBox Selector(string name) => form.Control<System.Windows.Controls.ComboBox>(name);
            var mode = Selector("InputModeSelector"); var destination = Selector("CompletionSelector");
            mode.SelectedIndex = (int)InputFocusMode.External; destination.SelectedIndex = (int)CompletionDestination.Clipboard;
            Check(!mode.IsEditable && !destination.IsEditable && mode.Items.Count == 3 && destination.Items.Count == 2 && destination.IsEnabled,
                "Mode and completion use single-selection dropdowns with three and two options");
            form.AddProgram("C:\\Games\\Example.exe"); mode.SelectedIndex = (int)InputFocusMode.Defocus;
            Check(!destination.IsEnabled, "Completion dropdown is disabled outside external input mode");
            form.AddProgram("c:\\games\\EXAMPLE.exe");
            Check(form.Draft.Programs.Count == 1 && mode.SelectedIndex == (int)InputFocusMode.Defocus, "Adding an existing path with different casing retains its unsaved independent configuration");
            var search = form.Control<Wpf.Ui.Controls.TextBox>("ProgramSearch"); search.Text = "no-match";
            Check(form.Control<System.Windows.Controls.ListBox>("ProgramList").Items.Count == 1 && form.Draft.Programs[0].Behavior.Mode == InputFocusMode.Defocus,
                "Filtering the program list preserves unsaved edits and keeps global configuration accessible");
            search.Text = ""; form.Control<System.Windows.Controls.ListBox>("ProgramList").SelectedIndex = 1;
            form.Click("RemoveProgram");
            Check(form.Draft.Programs.Count == 0 && form.Draft.Resolve("C:\\Games\\Example.exe") == form.Draft.Global,
                "Removing an independent configuration restores global inheritance");
            form.AddProgram("C:\\Games\\Example.exe"); mode.SelectedIndex = (int)InputFocusMode.Defocus;
            form.Control<System.Windows.Controls.ListBox>("ProgramList").SelectedIndex = 0;
            await Task.Delay(150);
            PanelSnapshot.Save(form, Path.Combine(root, "artifacts", "program-profiles-window.png"));
            form.Click("SaveProfiles"); var loaded = ProgramProfiles.Load(folder, out warning);
            Check(warning == null && loaded.Resolve("c:\\games\\EXAMPLE.exe").Mode == InputFocusMode.Defocus && loaded.Resolve("C:\\Other.exe") == new InputBehavior { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard }, "Per-executable overrides and global fallback survive save/reload with case-insensitive paths");
            using (var cancel = new ProgramProfilesForm(loaded, _ => throw new Exception("Cancel unexpectedly saved")))
            {
                cancel.Show(); cancel.Control<System.Windows.Controls.ComboBox>("InputModeSelector").SelectedIndex = (int)InputFocusMode.None;
                cancel.Click("CancelProfiles");
            }
            Check(ProgramProfiles.Load(folder, out _).Global == loaded.Global, "Canceling program configuration does not save edits");
            File.WriteAllText(Path.Combine(folder, "program-profiles.json"), "{\"Global\":{\"Mode\":999}}");
            Check(ProgramProfiles.Load(folder, out warning).Global.Mode == InputFocusMode.External && warning != null, "Malformed profile values fall back to external input with a warning");
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
