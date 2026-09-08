namespace GamePadT9;

// One coordinator shared by the interactive host and end-to-end UI tests.
internal sealed class InputSession(RimeEngine engine, InputMethodSwitcher? inputMethods = null)
{
    private readonly TsfClient tsf = new();
    private Target? boundTarget;
    private bool disableAfterCommit;
    private bool submissionUnconfirmed;
    private readonly SymbolMenu symbols = new();
    public bool Enabled { get; private set; }
    public bool Busy { get; private set; }
    public InputMode Mode { get; private set; }
    public EngineView View => Mode == InputMode.T9 ? (symbols.Visible ? symbols.View : engine.View) : EngineView.Empty;
    public string Message { get; private set; } = "View + Menu 开启输入";
    public string NumberHistory { get; private set; } = "";
    public event Action? Changed;
    public event Action<string>? Error;
    public async Task Enable(bool enabled)
    {
        if (Busy) { if (!enabled) disableAfterCommit = true; return; }
        if (Enabled == enabled) return;
        Busy = true;
        try
        {
            ClearComposition(); NumberHistory = "";
            if (enabled)
            {
                Message = inputMethods == null ? "右摇杆选区 · RT 输入 · Y 切换模式" : await inputMethods.StartAsync();
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
        await Enable(false);
        while (Busy) await Task.Delay(20);
        await Enable(false);
        if (inputMethods?.HasSavedProfiles == true)
            try { await inputMethods.RestoreAsync(); } catch (Exception ex) { Error?.Invoke(ex.Message); }
    }
    private void ClearComposition() { engine.Clear(); symbols.Close(); boundTarget = null; submissionUnconfirmed = false; }
    public async Task Handle(PadEvent action, int? candidateIndex = null)
    {
        if (action.Action == PadAction.Toggle) { await Enable(!Enabled && !Busy); return; }
        if (action.Action == PadAction.Disable) { await Enable(false); return; }
        if (!Enabled || Busy) return;
        if (action.Action == PadAction.Cancel)
        { ClearComposition(); Message = "候选已关闭"; Changed?.Invoke(); return; }
        if (engine.PendingCommit.Length != 0 || submissionUnconfirmed)
        { Message = "请检查上次提交结果，按 B 清除后继续"; Changed?.Invoke(); return; }
        if (action.Action == PadAction.SwitchMode)
        {
            await CheckFocus(); Mode = Mode == InputMode.T9 ? InputMode.Numeric : InputMode.T9;
            Message = Mode == InputMode.Numeric ? "RT 输入 1–9 · R3 输入 0" : "九键输入 · A 确认高亮候选";
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
            { boundTarget = target; symbols.Open(); Message = "A 输入标点 · LB / RB 选择 · 十字键左右翻页"; return; }
            if (Mode == InputMode.T9 && action.Action == PadAction.Region)
            { boundTarget = target; engine.InputRegion(action.Region); }
            else if (action.Action == PadAction.Backspace)
            {
                if (Mode == InputMode.T9 && engine.View.Preedit.Length > 0) engine.Process(0xFF08);
                else
                {
                    Busy = true; Changed?.Invoke();
                    var error = await tsf.Backspace(target.Value);
                    Message = error ?? "已删除前一个字符";
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
            Message = Mode == InputMode.T9 ? "A 选词 · LB / RB 翻选 · 十字键左右翻页" : "RT 输入 1–9 · R3 输入 0";
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
}
