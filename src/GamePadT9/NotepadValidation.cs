using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GamePadT9;

// Opens an owned, empty scratch document. UI Automation is used only to focus/read it;
// T9 text uses Rime -> TSF; numeric mode intentionally uses NumPad events.
internal sealed class NotepadValidation : Form
{
    private readonly string root;
    private readonly RimeEngine engine;
    private readonly bool useTestEditor;
    private Process? ownedEditor;
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    public NotepadValidation(string root, RimeEngine engine, bool useTestEditor = false)
    {
        this.root = root; this.engine = engine; this.useTestEditor = useTestEditor;
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        StartPosition = FormStartPosition.Manual; Location = new System.Drawing.Point(-2000, -2000);
        Shown += async (_, _) =>
        {
            try { await Run(); Result = 0; }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                File.WriteAllText(ReportPath, JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = false, error = ex.Message }, new JsonSerializerOptions { WriteIndented = true }));
            }
            finally { if (ownedEditor != null) { if (!ownedEditor.HasExited) ownedEditor.CloseMainWindow(); ownedEditor.Dispose(); } Close(); }
        };
    }
    private string ReportPath => Path.Combine(root, "artifacts", useTestEditor ? "editor-verification.json" : "notepad-verification.json");
    private async Task Run()
    {
        var name = "GamePadT9-TSF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(root, "artifacts", name + ".txt");
        File.WriteAllText(file, "", new UTF8Encoding(false));
        var start = new ProcessStartInfo(useTestEditor ? Path.Combine(root, "artifacts", "test-editor", "TestEditor.exe") : "notepad.exe") { UseShellExecute = true };
        start.ArgumentList.Add(file);
        var launched = Process.Start(start);
        if (useTestEditor) ownedEditor = launched;
        else launched?.Dispose();
        AutomationElement? window = null, editor = null;
        for (var i = 0; i < 100; i++)
        {
            window = FindWindow(name);
            if (window != null)
            {
                editor = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
                editor ??= window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (editor != null) break;
            }
            await Task.Delay(100);
        }
        if (window == null || editor == null) throw new Exception("未找到专用记事本文本框。");
        var hwnd = (nint)window.Current.NativeWindowHandle;
        SetForegroundWindow(hwnd); editor.SetFocus();
        await Task.Delay(200);
        var before = Read(editor);
        if (!string.IsNullOrWhiteSpace(before)) throw new Exception("验证文档非空，停止写入。");
        if (Program.Control("activate") != 0) throw new Exception("无法激活验证输入法。");
        SetForegroundWindow(hwnd); editor.SetFocus();
        Target? target = null;
        for (var i = 0; i < 100; i++)
        {
            target = TsfClient.FindTarget();
            if (target?.Foreground == hwnd) break;
            await Task.Delay(100);
        }
        if (target == null || target.Value.Foreground != hwnd) throw new Exception("记事本没有加载 TSF 桥接组件。请重新打开记事本或手动选择 GamePad T9 验证。");
        var session = new InputSession(engine);
        using var overlay = new MainForm(session);
        session.Enable(true);
        overlay.Present(4, 0);
        await Task.Delay(150);
        if (!overlay.Visible || TsfClient.GetForegroundWindow() != hwnd)
            throw new Exception("启用时浮窗未显示或抢走输入焦点。");
        // Exercise the same right-stick + RT event path as the interactive host.
        var controller = new Controller(); controller.Update(default, 0);
        (short x, short y)[] positions = [(22000, 0), (-22000, 0), (-22000, 0), (0, 22000), (22000, 0)];
        long tick = 10;
        foreach (var (x, y) in positions)
        {
            controller.Update(new Gamepad { RX = x, RY = y }, tick++);
            foreach (var action in controller.Update(new Gamepad { RX = x, RY = y, RT = 200 }, tick++))
                if (action.Action == PadAction.Region) await session.Handle(action);
            overlay.Present(controller.Region, 0);
        }
        var initialHighlight = session.View.Highlight;
        await session.Handle(new(PadAction.Next));
        if (session.View.Highlight == initialHighlight) throw new Exception("RB 未移动候选高亮。");
        await session.Handle(new(PadAction.Previous));
        if (session.View.Highlight != initialHighlight) throw new Exception("LB 未返回原候选。");
        await session.Handle(new(PadAction.PageNext));
        if (session.View.Page == 0) throw new Exception("十字键右未翻页。");
        await session.Handle(new(PadAction.PagePrevious));
        if (session.View.Page != 0) throw new Exception("十字键左未返回前页。");
        if (!Validation.Find(engine, "你好")) throw new Exception("原版小白候选中未找到你好。");
        overlay.Invalidate(); overlay.Update();
        await Task.Delay(150);
        if (overlay.DisplayedCandidateCount == 0) throw new Exception("浮窗未显示引擎候选。");
        var actualPoint = new Point(overlay.Left + 40, overlay.Top + 110);
        if (GetAncestor(WindowFromPoint(actualPoint), 2) != overlay.Handle)
            throw new Exception("九宫格被其他窗口覆盖。");
        CaptureOverlay(overlay, Path.Combine(root, "artifacts", "overlay-t9.png"));
        controller.Update(default, tick++);
        foreach (var action in controller.Update(new Gamepad { Buttons = Buttons.A }, tick++))
            if (action.Action == PadAction.Confirm) await session.Handle(action);
        await Task.Delay(200);
        var after = Read(editor);
        if (after.TrimEnd('\r', '\n') != "你好") throw new Exception("记事本读取结果不符：" + after);
        if (NumericInput.SentKeyEvents != 0) throw new Exception("九键路径发生了键盘模拟。");
        var rejected = await new TsfClient().Commit(target.Value with { Epoch = target.Value.Epoch + 1 }, "不应上屏");
        if (rejected == null || Read(editor) != after) throw new Exception("过期焦点令牌保护失败。");
        await session.Handle(new(PadAction.Region, 5));
        var draft = engine.View.Preedit;
        await session.Handle(new(PadAction.SwitchMode));
        if (overlay.DisplayedMode != InputMode.Numeric || overlay.DisplayedCandidateCount != 0) throw new Exception("数字面板切换失败。");
        for (var i = 0; i < 9; i++) await session.Handle(new(PadAction.Region, i));
        await session.Handle(new(PadAction.Region, 8, true));
        await Task.Delay(200);
        var numericAfter = Read(editor).TrimEnd('\r', '\n');
        if (numericAfter != "你好1234567890") throw new Exception("NumPad 数字输入结果不符：" + numericAfter + " / " + session.Message);
        overlay.Present(8, 0); overlay.Update();
        CaptureOverlay(overlay, Path.Combine(root, "artifacts", "overlay-numeric.png"));
        await session.Handle(new(PadAction.SwitchMode));
        if (engine.View.Preedit != draft) throw new Exception("切回九键后草稿丢失。");
        await session.Handle(new(PadAction.Cancel));
        if (!overlay.Visible || overlay.DisplayedCandidateCount != 0) throw new Exception("B 应关闭候选并保留九格。");
        var backspaceVerified = TsfClient.SupportsBackspace(TsfClient.FindTarget() ?? throw new Exception("TSF 目标丢失。"));
        if (useTestEditor && !backspaceVerified) throw new Exception("新建测试窗口仍加载旧组件。");
        var deleted = numericAfter;
        if (backspaceVerified)
        {
            await session.Handle(new(PadAction.Backspace));
            await Task.Delay(120);
            deleted = Read(editor).TrimEnd('\r', '\n');
            if (deleted != "你好123456789") throw new Exception("TSF 上屏退格失败：" + session.Message + "，读回=" + deleted);
            var freshTarget = TsfClient.FindTarget() ?? throw new Exception("TSF 目标丢失。");
            var editError = await new TsfClient().Commit(freshTarget, "😀");
            if (editError != null) throw new Exception(editError);
            await session.Handle(new(PadAction.Backspace));
            await Task.Delay(120);
            if (Read(editor).TrimEnd('\r', '\n') != deleted) throw new Exception("UTF-16 代理对退格失败。");
        }
        await session.Handle(new(PadAction.Region, 0));
        if (session.View.Candidates[0].Text != "，") throw new Exception("符号入口未显示常用标点。");
        await session.Handle(new(PadAction.Confirm));
        await Task.Delay(120);
        if (Read(editor).TrimEnd('\r', '\n') != deleted + "，") throw new Exception("符号确认未上屏。");
        if (NumericInput.SentKeyEvents != 20) throw new Exception("九键、标点或退格产生了额外键盘模拟。");
        await session.Handle(new(PadAction.Disable));
        if (overlay.Visible) throw new Exception("关闭输入后九格未隐藏。");
        var report = new
        {
            time = DateTimeOffset.Now, passed = true, completedAllChecks = backspaceVerified, scratchFile = file,
            targetApplication = useTestEditor ? "WPF test editor" : "Notepad",
            targetProcess = target.Value.Process, targetWindow = $"0x{hwnd:X}", before, after,
            input = "Synthetic XInput state samples -> production Controller -> original installed librime -> production TSF client",
            physicalController = false, t9KeyboardSimulation = false, numericKeyEvents = NumericInput.SentKeyEvents,
            staleTargetRejected = true,
            overlayVisibleWithoutFocusSteal = true, overlayAboveTarget = true, candidatePanelRendered = true,
            candidateHighlightNavigation = true, candidatePaging = true,
            numericAfter, draftPreserved = true, backspaceAfter = backspaceVerified ? deleted : null, surrogatePairBackspace = backspaceVerified,
            symbolMenuAndCommit = true,
            cancelKeepsGrid = true, disableHidesGrid = true,
            skipped = backspaceVerified ? Array.Empty<string>() : ["Already-open Notepad has old native DLL: restart it to test committed-text/emoji backspace"],
            proof = "TSF edit-session receipts AND independent UI Automation readback of target document"
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(ReportPath, json, new UTF8Encoding(false));
        Console.WriteLine(json);
    }
    private static void CaptureOverlay(Form form, string path)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        using var graphics = Graphics.FromImage(bitmap);
        var dc = graphics.GetHdc();
        try { if (!PrintWindow(form.Handle, dc, 2)) throw new Exception("浮窗截图失败。"); }
        finally { graphics.ReleaseHdc(dc); }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    private static string Read(AutomationElement editor)
    {
        if (editor.TryGetCurrentPattern(TextPattern.Pattern, out var text)) return ((TextPattern)text).DocumentRange.GetText(-1);
        if (editor.TryGetCurrentPattern(ValuePattern.Pattern, out var value)) return ((ValuePattern)value).Current.Value;
        throw new Exception("记事本文本框不支持读取验证。");
    }
    private static AutomationElement? FindWindow(string title)
    {
        foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            try { if (candidate.Current.Name.Contains(title, StringComparison.Ordinal)) return candidate; }
            catch (ElementNotAvailableException) { }
        }
        return null;
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint hwnd);
}
