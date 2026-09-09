using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace GamePadT9;

internal sealed class FocusedInputSession : IDisposable
{
    private readonly RimeEngine engine;
    private readonly InputMethodSwitcher inputMethods;
    private readonly InputWindow original;
    private readonly int[]? originalControl;
    private readonly InputBehavior behavior;
    private readonly Func<bool> neutral;
    private readonly Func<bool> buttonsReleased;
    private readonly Func<Rectangle> overlayBounds;
    private readonly Action<string> saveDraft;
    private readonly TsfClient tsf = new();
    private readonly SymbolMenu symbols = new();
    private readonly FocusInputForm form;
    private readonly Form? overlay;
    private CancellationTokenSource? operation;
    private bool closing, uncertain, recoveryRequired, disposed, shuttingDown;
    public bool Enabled { get; private set; }
    public bool Busy { get; private set; }
    public InputMode Mode { get; private set; }
    public EngineView View => Mode == InputMode.T9 ? (symbols.Visible ? symbols.View : engine.View) : EngineView.Empty;
    public string Message { get; private set; } = "";
    public string NumberHistory { get; private set; } = "";
    internal bool External => behavior.Mode == InputFocusMode.External;
    private nint FocusHandle => External ? form.ExistingHandle : overlay is { IsHandleCreated: true } ? overlay.Handle : 0;
    internal bool OwnsFocus => FocusHandle != 0 && TsfClient.GetForegroundWindow() == FocusHandle;
    internal bool HasRetainedText => RecoveryText().Length > 0;
    internal FocusInputForm Form => form;
    internal string Draft => form.Editor.Text;
    internal event Action? Changed;
    internal event Action<string>? Error;
    internal FocusedInputSession(RimeEngine engine, InputMethodSwitcher inputMethods, InputWindow original, InputBehavior behavior,
        Func<bool> neutral, Func<bool> buttonsReleased, Func<Rectangle> overlayBounds, Form? overlay, string draft, Action<string> saveDraft)
    {
        this.engine = engine; this.inputMethods = inputMethods; this.original = original; this.behavior = behavior;
        this.neutral = neutral; this.overlayBounds = overlayBounds; this.saveDraft = saveDraft;
        this.buttonsReleased = buttonsReleased;
        try { originalControl = AutomationElement.FocusedElement?.GetRuntimeId(); }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
        if (!External && overlay == null) throw new InvalidOperationException("缺少九宫格输入面板。");
        form = new FocusInputForm(External, behavior.Completion, System.IO.Path.GetFileName(ProgramProfiles.Executable(original)) ?? "目标程序");
        this.overlay = overlay;
        form.Editor.Text = draft; form.Editor.SelectionStart = draft.Length;
        if (!External && draft.Length > 0) { pendingText = draft; recoveryRequired = true; }
        form.Editor.TextChanged += (_, _) => { if (External) saveDraft(Draft); };
        form.CompleteRequested += async () => await Handle(new(PadAction.Complete));
        form.CancelRequested += async () => await Enable(false);
        form.ClearRequested += ClearRetainedText;
        form.CopyRequested += async () => await CopyRetainedTextAsync();
        form.Activated += (_, _) => { if (Enabled && !Busy) { Message = "输入已就绪"; Notify(); } };
    }
    internal void ClearRetainedText()
    {
        if (Busy) return;
        engine.Clear(); symbols.Close(); pendingText = ""; recoveryRequired = uncertain = false;
        form.Editor.Clear(); saveDraft(""); Message = "草稿已清空，可以重新输入"; Notify();
    }
    internal async Task CopyRetainedTextAsync()
    {
        if (Busy) return;
        try { await CopyAsync(External ? Draft : RecoveryText(), CancellationToken.None); Message = "已复制保留的文字，请核对目标后再操作"; }
        catch (Exception ex) { Message = ex.Message; }
        Notify();
    }
    internal async Task Enable(bool enabled)
    {
        if (Busy) { if (!enabled) { closing = true; operation?.Cancel(); } return; }
        if (Enabled == enabled)
        {
            if (!enabled && inputMethods.HasSavedProfiles)
            {
                Busy = true;
                try { await RestoreProfileAsync(); }
                finally { Busy = false; Notify(); }
            }
            return;
        }
        Busy = true;
        try
        {
            if (enabled)
            {
                engine.Clear(); symbols.Close();
                if (!original.IsAlive || InputMethodSwitcher.Foreground() != original) throw new InvalidOperationException("原目标焦点已变化，请重新开启输入。");
                if (!External) await inputMethods.StartAsync();
                else if (behavior.Completion == CompletionDestination.Target) await inputMethods.CaptureAsync(original);
                Enabled = true; Notify();
                if (External) { form.PlaceAbove(overlayBounds()); form.Show(); }
                var focused = await FocusPanelAsync();
                Message = focused ? (External ? "选词后可继续编辑，长按确认键完成输入" : "选词后自动切回目标填入，再返回此面板") : "未能获得焦点，请点击输入面板后继续";
            }
            else await CloseCoreAsync();
        }
        catch (Exception ex)
        {
            Message = ex.Message;
            if (!Enabled) { form.Hide(); await RestoreProfileAsync(); Error?.Invoke(Message); }
        }
        finally { Busy = false; Notify(); if (closing && Enabled) { closing = false; await Enable(false); } }
    }
    internal Task CheckFocus()
    {
        if (Enabled && !Busy && !OwnsFocus && Message != "输入已暂停：请返回输入面板继续")
        { Message = "输入已暂停：请返回输入面板继续"; Notify(); }
        return Task.CompletedTask;
    }
    internal async Task ShutdownAsync()
    {
        shuttingDown = true;
        closing = true; operation?.Cancel();
        while (Busy) await Task.Delay(20);
        closing = false;
        Busy = true;
        try { await CloseCoreAsync(waitForRelease: false); }
        finally { Busy = false; Notify(); }
    }
    private void Notify()
    {
        form.UpdateState(Message, External && !Busy && !recoveryRequired, Enabled && !Busy && !recoveryRequired, Busy);
        if (!External) form.Editor.Text = RecoveryText();
        Changed?.Invoke();
    }
    private string RecoveryText() => engine.PendingCommit.Length > 0 ? engine.PendingCommit : pendingText;
    private string pendingText = "";
    internal async Task Handle(PadEvent action, int? candidateIndex = null)
    {
        // The menu chord finishes an external draft through the same guarded
        // commit path as long A. Explicit cancellation still retains the draft.
        if (action.Action == PadAction.Toggle && External) action = new(PadAction.Complete);
        if (action.Action is PadAction.Toggle or PadAction.Disable) { await Enable(false); return; }
        if (!Enabled || Busy) return;
        if (!OwnsFocus) { Message = "输入已暂停：请返回输入面板继续"; Notify(); return; }
        if (recoveryRequired) { Message = External ? "上次提交结果需核对；文字已保留，可复制后关闭输入" : "文字已保留，请核对目标；右键面板可复制或清空"; Notify(); return; }
        if (action.Action == PadAction.Cancel)
        {
            if (pendingText.Length > 0 || engine.PendingCommit.Length > 0) { Message = "待提交文字已保留，请复制或关闭输入"; Notify(); return; }
            engine.Clear(); symbols.Close(); Message = External ? "候选已关闭，输入栏文字保留" : "候选已关闭"; Notify(); return;
        }
        if (action.Action == PadAction.SwitchMode)
        { Mode = Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9; Message = "已切换输入模式"; Notify(); return; }
        Busy = true; operation = new(); var token = operation.Token;
        try
        {
            if (action.Action == PadAction.Complete)
            {
                if (!External) return;
                if (symbols.Visible) { Insert(symbols.Select(null)); symbols.Close(); }
                if (engine.View.Preedit.Length > 0) { engine.Confirm(); AppendCommit(); }
                if (engine.View.Preedit.Length > 0) { Mode = InputMode.T9; Message = "还有未确认的候选，请继续选词后完成"; return; }
                await CompleteAsync(token); return;
            }
            if (Mode == InputMode.Numeric && action.Action == PadAction.Region)
            {
                var digit = action.StickClick ? 0 : action.Region + 1;
                if (External) Insert(digit.ToString());
                else
                {
                    pendingText = digit.ToString();
                    await InTargetAsync(async () =>
                    {
                        var before = NumericInput.SentKeyEvents;
                        try { NumericInput.SendDigit(InputMode.Numeric, digit, original.Root); await Task.Delay(80); RequireTarget(); return null; }
                        catch { uncertain = NumericInput.SentKeyEvents != before; throw; }
                    }, token, requireCenteredSticks: false);
                    pendingText = "";
                }
                NumberHistory = (NumberHistory + digit); if (NumberHistory.Length > 24) NumberHistory = NumberHistory[^24..];
                Message = External ? "数字已加入输入栏" : "数字已填入目标"; return;
            }
            if (Mode == InputMode.T9 && symbols.Visible)
            {
                switch (action.Action)
                {
                    case PadAction.Previous: symbols.Move(-1); return;
                    case PadAction.Next: symbols.Move(1); return;
                    case PadAction.PagePrevious: symbols.Page(-1); return;
                    case PadAction.PageNext: symbols.Page(1); return;
                    case PadAction.Backspace: symbols.Close(); return;
                    case PadAction.Confirm:
                        var text = symbols.Select(candidateIndex);
                        await CommitAsync(text, token); symbols.Close(); return;
                    case PadAction.Region: symbols.Close(); break;
                }
            }
            if (Mode == InputMode.T9 && action.Action == PadAction.Region && action.Region == 0 && engine.View.Preedit.Length == 0)
            { symbols.Open(); Message = "请选择标点"; return; }
            if (Mode == InputMode.T9 && action.Action == PadAction.Region) engine.InputRegion(action.Region);
            else if (action.Action == PadAction.Backspace)
            {
                if (Mode == InputMode.T9 && engine.View.Preedit.Length > 0) engine.Process(0xFF08);
                else if (External) DeleteDraft();
                else await InTargetAsync(async () =>
                {
                    var error = await tsf.Backspace(RequireTarget());
                    if (tsf.LastBackspaceSimulated) await Task.Delay(80);
                    return error;
                }, token);
            }
            else if (Mode == InputMode.T9)
            {
                switch (action.Action)
                {
                    case PadAction.Confirm: engine.Confirm(candidateIndex); break;
                    case PadAction.Previous: engine.Process(0xFF52); break;
                    case PadAction.Next: engine.Process(0xFF54); break;
                    case PadAction.PagePrevious: engine.Process(0xFF55); break;
                    case PadAction.PageNext: engine.Process(0xFF56); break;
                }
            }
            Notify();
            if (engine.PendingCommit.Length > 0)
            { await CommitAsync(engine.PendingCommit, token); engine.AcknowledgeCommit(); }
            Message = External ? "短按确认键选词，长按完成输入" : "选词后自动填入目标";
        }
        catch (OperationCanceledException) { Message = "操作已取消，未完成的文字保留"; }
        catch (Exception ex)
        {
            Message = ex.Message;
            if (!External && RecoveryText().Length > 0) recoveryRequired = true;
            if (uncertain) { recoveryRequired = true; Message += "；结果待核对，已停止再次提交，可复制保留的文字"; }
            if (!External && recoveryRequired) Message += "；右键面板可复制或清空";
        }
        finally
        {
            operation.Dispose(); operation = null; Busy = false; Notify();
            if (closing && !shuttingDown) { closing = false; await Enable(false); }
        }
    }
    private void AppendCommit()
    {
        if (engine.PendingCommit.Length == 0) return;
        Insert(engine.PendingCommit); engine.AcknowledgeCommit();
    }
    private void Insert(string text)
    {
        var editor = form.Editor;
        var start = editor.SelectionStart;
        editor.SelectedText = text;
        // WPF retains the inserted selection; collapse it so subsequent gamepad
        // commits append at the caret instead of replacing the previous word.
        editor.Select(start + text.Length, 0);
        var line = editor.GetLineIndexFromCharacterIndex(editor.CaretIndex);
        if (line >= 0) editor.ScrollToLine(line);
    }
    private void DeleteDraft()
    {
        var editor = form.Editor;
        if (editor.SelectionLength > 0) { editor.SelectedText = ""; return; }
        var end = editor.SelectionStart; if (end == 0) return;
        var starts = StringInfo.ParseCombiningCharacters(editor.Text);
        var start = starts.Last(i => i < end);
        editor.Select(start, end - start); editor.SelectedText = "";
    }
    private async Task CommitAsync(string text, CancellationToken token)
    {
        if (External) { Insert(text); return; }
        pendingText = text;
        await InTargetAsync(() => tsf.Commit(RequireTarget(), text), token, requireCenteredSticks: false);
        pendingText = "";
    }
    private async Task CompleteAsync(CancellationToken token)
    {
        var text = Draft;
        if (text.Length == 0) { await CloseCoreAsync(); return; }
        if (behavior.Completion == CompletionDestination.Clipboard)
        {
            await CopyAsync(text, token);
            Message = "文字已复制到剪切板";
            // Clipboard success is final even if returning focus is later denied.
            form.Editor.Clear(); await CloseCoreAsync(); return;
        }
        // The existing TSF bridge accepts one bounded transaction; never split and risk partial duplicates.
        if (text.Length > 1024) throw new InvalidOperationException("目标单次最多接收 1024 个 UTF-16 单元；请缩短文字或使用复制保留文字按钮。");
        await InTargetAsync(() => tsf.Commit(RequireTarget(), text), token, returnToPanel: false);
        form.Editor.Clear(); Message = "文字已填入目标程序";
        await CloseCoreAsync();
    }
    private bool TargetMatches()
    {
        if (!original.IsAlive || InputMethodSwitcher.Foreground() != original) return false;
        if (originalControl == null) return true;
        try { return AutomationElement.FocusedElement?.GetRuntimeId().SequenceEqual(originalControl) == true; }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { return false; }
    }
    private Target RequireTarget()
    {
        if (!TargetMatches()) throw new InvalidOperationException("原目标文本框已变化，文字保留，未提交。");
        return TsfClient.FindTarget() ?? throw new InvalidOperationException("目标文本框未提供输入通道，文字保留，可复制后手动粘贴。");
    }
    private async Task WaitForReleaseAsync(CancellationToken token, bool requireCenteredSticks = true)
    {
        Message = requireCenteredSticks ? "请松开手柄按键并让摇杆回中，随后返回目标" : "请松开手柄按键与扳机，随后填入目标"; Notify();
        for (var i = 0; i < 400; i++)
        {
            token.ThrowIfCancellationRequested();
            if (requireCenteredSticks ? neutral() : buttonsReleased()) return;
            await Task.Delay(20, token);
        }
        throw new InvalidOperationException("等待手柄释放超时，文字已保留。");
    }
    private async Task InTargetAsync(Func<Task<string?>> edit, CancellationToken token, bool returnToPanel = true, bool requireCenteredSticks = true)
    {
        uncertain = false;
        await WaitForReleaseAsync(token, requireCenteredSticks);
        if (!OwnsFocus) throw new InvalidOperationException("您已切换到其他窗口，已停止自动填入。");
        if (!original.IsAlive) throw new InvalidOperationException("原目标窗口已关闭，文字已保留。");
        try
        {
            SetForegroundWindow(original.Root);
            for (var i = 0; i < 40 && !TargetMatches(); i++) await Task.Delay(20, token);
            if (!TargetMatches()) throw new InvalidOperationException("无法恢复原文本框焦点，文字已保留。");
            if (!inputMethods.HasSavedProfiles) await inputMethods.StartAsync();
            else await inputMethods.FollowAsync();
            token.ThrowIfCancellationRequested(); RequireTarget();
            var error = await edit(); // Once dispatched, always await the receipt even if cancellation arrives.
            uncertain |= tsf.LastOutcomeUncertain;
            if (error != null) throw new InvalidOperationException(error);
        }
        catch
        {
            if (Enabled && (TsfClient.GetForegroundWindow() == original.Root || OwnsFocus)) await FocusPanelAsync();
            throw;
        }
        finally
        {
            if (returnToPanel && Enabled && !closing && TsfClient.GetForegroundWindow() == original.Root)
            {
                var returned = await FocusPanelAsync();
                if (!returned) Message = "文字已填入；请点击输入面板继续";
            }
        }
    }
    private async Task<bool> FocusPanelAsync()
    {
        if (External)
        {
            if (!form.IsVisible) form.Show();
            form.PlaceAbove(overlayBounds()); form.Activate();
        }
        else { if (!overlay!.Visible) overlay.Show(); overlay.Activate(); }
        SetForegroundWindow(FocusHandle);
        if (!OwnsFocus && InputMethodSwitcher.Foreground() == original)
        {
            try
            {
                var granted = await inputMethods.RunAsync(original, "grant-focus", focusDestination: FocusHandle);
                if (granted.Ok && InputMethodSwitcher.Foreground() == original) SetForegroundWindow(FocusHandle);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { Message = ex.Message; }
        }
        if (External) form.Editor.Focus();
        for (var i = 0; i < 25 && !OwnsFocus; i++) await Task.Delay(20);
        return OwnsFocus;
    }
    private async Task CloseCoreAsync(bool waitForRelease = true)
    {
        var returnFocus = OwnsFocus;
        if (returnFocus && waitForRelease)
        {
            try { await WaitForReleaseAsync(CancellationToken.None); }
            catch (Exception ex) { Message = ex.Message; return; }
        }
        // Preserve any unacknowledged text before clearing the engine on the next session.
        if (!External && RecoveryText().Length > 0) saveDraft(RecoveryText());
        string? focusError = null;
        if (returnFocus && OwnsFocus && original.IsAlive)
        {
            // Restore foreground first. Activating the target after restoring its IME can
            // overwrite that restoration with a queued focus/profile change.
            SetForegroundWindow(original.Root);
            for (var i = 0; i < 40 && InputMethodSwitcher.Foreground() != original; i++)
            {
                var foreground = TsfClient.GetForegroundWindow();
                if (foreground != 0 && foreground != original.Root && !OwnsFocus) break;
                await Task.Delay(20);
            }
            if (InputMethodSwitcher.Foreground() != original) focusError = "未能恢复原文本框焦点，请返回目标程序核对输入法。";
        }
        Enabled = false;
        form.Hide();
        Notify(); // Hide the grid too, including disconnect/settings/shutdown paths.
        var restored = await RestoreProfileAsync();
        if (restored) Message = "输入已关闭";
        if (focusError != null) { Message += "；" + focusError; Error?.Invoke(focusError); }
    }
    private async Task<bool> RestoreProfileAsync()
    {
        try { if (inputMethods.HasSavedProfiles) await inputMethods.RestoreAsync(); return true; }
        catch (Exception ex) { Message += "；" + ex.Message; Error?.Invoke(ex.Message); return false; }
    }
    internal static async Task CopyAsync(string text, CancellationToken token)
    {
        if (text.Length == 0) throw new InvalidOperationException("没有可复制的文字。");
        for (var i = 0; ; i++)
        {
            token.ThrowIfCancellationRequested();
            try { Clipboard.SetText(text, TextDataFormat.UnicodeText); return; }
            catch (ExternalException) when (i < 5) { await Task.Delay(60, token); }
        }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (External) saveDraft(Draft); else if (RecoveryText().Length > 0) saveDraft(RecoveryText());
        operation?.Cancel(); form.AllowClose = true; form.Dispose();
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
