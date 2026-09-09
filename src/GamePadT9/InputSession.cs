namespace GamePadT9;

// One coordinator shared by the interactive host and end-to-end UI tests.
internal sealed class InputSession(RimeEngine engine, InputMethodSwitcher? inputMethods = null) : IDisposable
{
    private FocusedInputSession? focused;
    private readonly Dictionary<string, string> drafts = new(StringComparer.OrdinalIgnoreCase);
    internal Func<(InputBehavior Behavior, InputWindow Window, string Key)>? FocusConfiguration { get; set; }
    internal Func<bool> ControlsReleased { get; set; } = () => true;
    internal Func<bool>? ButtonsReleased { get; set; }
    internal Func<Rectangle> OverlayBounds { get; set; } = () => new(100, 300, 780, 520);
    internal Form? FocusOverlay { get; set; }
    internal FocusedInputSession? Focused => focused;
    internal bool ExternalInput => focused?.External == true;
    private bool enabled, busy;
    private InputMode mode;
    private string message = "同时按下两枚菜单键开启输入", numberHistory = "";
    private readonly TsfClient tsf = new();
    private Target? boundTarget;
    private bool disableAfterCommit;
    private bool submissionUnconfirmed;
    private readonly SymbolMenu symbols = new();
    public bool Enabled { get => focused?.Enabled ?? enabled; private set => enabled = value; }
    public bool Busy { get => focused?.Busy ?? busy; private set => busy = value; }
    internal UserSettings Preferences { get; set; } = new();
    private string NumericHint => "数字模式：扳机输入 1–9，按下所选摇杆输入 0";
    public InputMode Mode { get => focused?.Mode ?? mode; private set => mode = value; }
    public EngineView View => focused?.View ?? (Mode == InputMode.T9 ? (symbols.Visible ? symbols.View : engine.View) : EngineView.Empty);
    public string Message { get => focused?.Message ?? message; private set => message = value; }
    public string NumberHistory { get => focused?.NumberHistory ?? numberHistory; private set => numberHistory = value; }
    public event Action? Changed;
    public event Action<string>? Error;
    public async Task Enable(bool enabled)
    {
        if (enabled && !Enabled && !Busy && FocusConfiguration != null)
        {
            focused?.Dispose(); focused = null;
            try
            {
                var config = FocusConfiguration();
                if (config.Behavior.Mode != InputFocusMode.None)
                {
                    if (inputMethods == null) throw new InvalidOperationException("缺少输入法切换组件。");
                    focused = new(engine, inputMethods, config.Window, config.Behavior, ControlsReleased, ButtonsReleased ?? ControlsReleased, OverlayBounds, FocusOverlay,
                        drafts.GetValueOrDefault(config.Key, ""), text => drafts[config.Key] = text);
                    focused.Form.ApplySettings(Preferences);
                    focused.Changed += () => Changed?.Invoke();
                    focused.Error += text => Error?.Invoke(text);
                }
            }
            catch (Exception ex) { Message = ex.Message; Error?.Invoke(Message); Changed?.Invoke(); return; }
        }
        if (focused != null) { await focused.Enable(enabled); return; }
        if (Busy) { if (!enabled) disableAfterCommit = true; return; }
        if (Enabled == enabled) return;
        Busy = true;
        try
        {
            ClearComposition(); NumberHistory = "";
            if (enabled)
            {
                Message = inputMethods == null ? $"{Preferences.StickLabel}选择区域，按所选扳机输入" : await inputMethods.StartAsync();
                Enabled = true;
            }
            else
            {
                Enabled = false; Changed?.Invoke();
                if (inputMethods != null) await inputMethods.RestoreAsync();
                Message = "输入已关闭，已恢复原输入法";
            }
        }
        catch (Exception ex)
        {
            Enabled = false; Message = ex.Message;
            if (enabled && inputMethods != null)
                try { await inputMethods.RestoreAsync(); } catch (Exception restore) { Message += "；" + restore.Message; }
            Error?.Invoke(Message);
        }
        finally
        {
            Busy = false; Changed?.Invoke();
            if (disableAfterCommit) { disableAfterCommit = false; await Enable(false); }
        }
    }
    public async Task CheckFocus()
    {
        if (focused != null) { await focused.CheckFocus(); return; }
        if (!Enabled || Busy || engine.PendingCommit.Length != 0 || submissionUnconfirmed) return;
        if (inputMethods?.NeedsForegroundSwitch == true)
        {
            Busy = true;
            try { ClearComposition(); Message = await inputMethods.FollowAsync() ?? Message; }
            catch (Exception ex) { Message = ex.Message; Error?.Invoke(Message); }
            finally
            {
                Busy = false; Changed?.Invoke();
                if (disableAfterCommit) { disableAfterCommit = false; await Enable(false); }
            }
        }
        if (boundTarget == null) return;
        if (TsfClient.FindTarget() != boundTarget)
        { ClearComposition(); Message = "焦点已切换，未提交的编码已取消"; Changed?.Invoke(); }
    }
    public async Task ShutdownAsync()
    {
        if (focused != null) { await focused.ShutdownAsync(); return; }
        await Enable(false);
        while (Busy) await Task.Delay(20);
        await Enable(false);
        if (inputMethods?.HasSavedProfiles == true)
            try { await inputMethods.RestoreAsync(); } catch (Exception ex) { Error?.Invoke(ex.Message); }
    }
    private void ClearComposition() { engine.Clear(); symbols.Close(); boundTarget = null; submissionUnconfirmed = false; }
    public async Task Handle(PadEvent action, int? candidateIndex = null)
    {
        if (action.Action == PadAction.Toggle)
        {
            if (focused is { External: true, Enabled: true }) await focused.Handle(action, candidateIndex);
            else await Enable(!Enabled && !Busy);
            return;
        }
        if (action.Action == PadAction.Disable) { await Enable(false); return; }
        if (focused != null) { await focused.Handle(action, candidateIndex); return; }
        if (action.Action == PadAction.Complete) return;
        if (!Enabled || Busy) return;
        if (action.Action == PadAction.Cancel)
        { ClearComposition(); Message = "候选已关闭"; Changed?.Invoke(); return; }
        if (engine.PendingCommit.Length != 0 || submissionUnconfirmed)
        { Message = "请检查上次提交结果，关闭候选后继续"; Changed?.Invoke(); return; }
        if (action.Action == PadAction.SwitchMode)
        {
            await CheckFocus(); Mode = Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9;
            Message = Mode == InputMode.Numeric ? NumericHint : "九键模式：请选择字母组或确认高亮候选";
            Changed?.Invoke(); return;
        }
        try
        {
            var target = TsfClient.FindTarget();
            if (target == null) { Message = "请选择小白 T9 或 GamePad T9；键盘组词请先完成或取消"; return; }
            if (boundTarget != null && boundTarget != target) ClearComposition();
            if (Mode == InputMode.Numeric && action.Action == PadAction.Region)
            {
                var digit = action.StickClick ? 0 : action.Region + 1;
                NumericInput.SendDigit(Mode, digit, target.Value.Foreground);
                NumberHistory += digit;
                if (NumberHistory.Length > 24) NumberHistory = NumberHistory[^24..];
                Message = $"已输入 {digit}"; return;
            }
            if (Mode == InputMode.T9 && symbols.Visible)
            {
                switch (action.Action)
                {
                    case PadAction.Previous: symbols.Move(-1); return;
                    case PadAction.Next: symbols.Move(1); return;
                    case PadAction.PagePrevious: symbols.Page(-1); return;
                    case PadAction.PageNext: symbols.Page(1); return;
                    case PadAction.Backspace: symbols.Close(); boundTarget = null; return;
                    case PadAction.Confirm:
                        Busy = true; Changed?.Invoke();
                        var text = symbols.Select(candidateIndex);
                        var error = await tsf.Commit(target.Value, text);
                        if (error == null) { symbols.Close(); boundTarget = null; Message = $"已输入：{text}"; }
                        else { submissionUnconfirmed = true; Message = error; }
                        return;
                    case PadAction.Region: symbols.Close(); break;
                }
            }
            if (Mode == InputMode.T9 && action.Action == PadAction.Region && action.Region == 0 && engine.View.Preedit.Length == 0)
            { boundTarget = target; symbols.Open(); Message = "请选择标点符号"; return; }
            if (Mode == InputMode.T9 && action.Action == PadAction.Region)
            { boundTarget = target; engine.InputRegion(action.Region); }
            else if (action.Action == PadAction.Backspace)
            {
                if (Mode == InputMode.T9 && engine.View.Preedit.Length > 0) engine.Process(0xFF08);
                else
                {
                    Busy = true; Changed?.Invoke();
                    var error = await tsf.Backspace(target.Value);
                    Message = error ?? (tsf.LastBackspaceSimulated ? "已发送 Backspace" : "已删除前一个字符");
                    submissionUnconfirmed = error != null && tsf.LastOutcomeUncertain;
                    if (error == null && Mode == InputMode.Numeric && NumberHistory.Length > 0) NumberHistory = NumberHistory[..^1];
                    return;
                }
            }
            else if (Mode == InputMode.T9 && boundTarget != null)
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
            Message = Mode == InputMode.T9 ? "请选择候选词，可继续选字母组或翻页" : NumericHint;
            // Render candidates before waiting for an asynchronous edit session.
            Changed?.Invoke();
            if (engine.PendingCommit.Length > 0 && boundTarget is Target commitTarget)
            {
                Busy = true;
                var text = engine.PendingCommit;
                var error = await tsf.Commit(commitTarget, text);
                if (error == null) { engine.AcknowledgeCommit(); Message = $"已输入：{text}"; }
                else Message = error;
            }
            if (engine.View.Preedit.Length == 0 && engine.PendingCommit.Length == 0) boundTarget = null;
        }
        catch (Exception ex) { Message = ex.Message; }
        finally
        {
            Busy = false;
            if (disableAfterCommit) { disableAfterCommit = false; await Enable(false); }
            Changed?.Invoke();
        }
    }
    public void Dispose() { focused?.Dispose(); }
}
