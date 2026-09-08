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
    private readonly bool isolated, editorX86, standalone, proxy;
    private Process? ownedEditor;
    private InputSession? activeSession;
    private AutomationElement? scratchWindow;
    private string? scratchName;
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    public NotepadValidation(string root, RimeEngine engine, bool useTestEditor = false, bool isolated = false, bool editorX86 = false, bool standalone = false, bool proxy = false)
    {
        this.root = root; this.engine = engine; this.useTestEditor = useTestEditor;
        this.isolated = isolated; this.editorX86 = editorX86; this.standalone = standalone; this.proxy = proxy;
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
            finally
            {
                if (activeSession != null) await activeSession.ShutdownAsync();
                if (ownedEditor != null) { if (!ownedEditor.HasExited) ownedEditor.CloseMainWindow(); ownedEditor.Dispose(); }
                Close();
            }
        };
    }
    private string ReportPath => Path.Combine(root, "artifacts", useTestEditor ? $"editor{(proxy ? "-proxy" : standalone ? "-standalone" : "")}{(editorX86 ? "-x86" : "")}{(isolated ? "-isolated" : "")}-verification.json" : "notepad-verification.json");
    private async Task Run()
    {
        var name = "GamePadT9-TSF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(root, "artifacts", name + ".txt");
        File.WriteAllText(file, "", new UTF8Encoding(false));
        var editorFolder = "test-editor" + (proxy ? "-proxy" : standalone ? "-standalone" : isolated ? "" : "-installed") + (editorX86 ? "-x86" : "");
        var start = new ProcessStartInfo(useTestEditor ? Path.Combine(root, "artifacts", editorFolder, "TestEditor.exe") : "notepad.exe") { UseShellExecute = true };
        start.ArgumentList.Add(file);
        if (isolated) start.ArgumentList.Add("--isolated");
        if (standalone) start.ArgumentList.Add("--standalone");
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
        scratchWindow = window; scratchName = name;
        var hwnd = (nint)window.Current.NativeWindowHandle;
        SetForegroundWindow(hwnd); editor.SetFocus();
        await Task.Delay(200);
        var before = Read(editor);
        if (!string.IsNullOrWhiteSpace(before)) throw new Exception("验证文档非空，停止写入。");
        // Notepad exercises the production chord and switches its focused editor thread.
        // WPF tests retain their process-local profile setup for component verification.
        var inputMethods = useTestEditor ? null : new InputMethodSwitcher(root);
        var inputWindow = InputMethodSwitcher.Foreground() ?? throw new Exception("未找到编辑区焦点。");
        var frameThread = TsfClient.GetWindowThreadProcessId(hwnd, out var frameProcess);
        var frame = new InputWindow(hwnd, frameProcess, frameThread);
        var inputProfileBefore = inputMethods == null ? null : await inputMethods.RunAsync(inputWindow, "query");
        var frameProfileBefore = inputMethods == null ? null : await inputMethods.RunAsync(frame, "query");
        var session = new InputSession(engine, inputMethods); activeSession = session;
        using var overlay = new MainForm(session);
        var controller = new Controller(); controller.Update(default, 0);
        foreach (var action in controller.Update(new Gamepad { Buttons = Buttons.View | Buttons.Menu }, 1)) await session.Handle(action);
        controller.Update(default, 2);
        if (!session.Enabled) throw new Exception("开启输入失败：" + session.Message);
        var inputProfileAfter = inputMethods == null ? null : await inputMethods.RunAsync(inputWindow, "query");
        var frameProfileAfter = inputMethods == null ? null : await inputMethods.RunAsync(frame, "query");
        if (inputMethods != null && (!inputProfileBefore!.Ok || !inputProfileAfter!.Ok ||
            (inputProfileAfter.After.Clsid != new Guid("595B67E9-48A3-4C82-B7B1-64E4A35C9D92") &&
             inputProfileAfter.After.Clsid != new Guid("A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A"))))
            throw new Exception("编辑区未切换到可用的手柄输入法：" + session.Message);
        if (inputMethods != null && inputWindow.Thread != frameThread && frameProfileAfter!.After != frameProfileBefore!.Before)
            throw new Exception("切换编辑线程时意外修改了主窗口线程输入法。");
        Target? target = null;
        for (var i = 0; i < 100; i++)
        {
            target = TsfClient.FindTarget();
            if (target?.Foreground == hwnd) break;
            await Task.Delay(100);
        }
        if (target == null || target.Value.Foreground != hwnd) throw new Exception("目标程序没有加载手柄输入组件。请重新打开程序并选择小白 T9 或 GamePad T9。");
        if (inputMethods != null && (target.Value.Backend == InputBackend.Standalone) !=
            (inputProfileAfter!.After.Clsid == new Guid("595B67E9-48A3-4C82-B7B1-64E4A35C9D92")))
            throw new Exception("实际输入端点与自动切换后的输入法不一致。");
        if (useTestEditor && target.Value.Backend != (standalone ? InputBackend.Standalone : InputBackend.Xiaobai))
            throw new Exception("测试连接到了错误的输入法入口。");
        string? componentSha256 = null, componentPath = null;
        if (useTestEditor)
        {
            for (var i = 0; i < 40 && !File.Exists(file + ".component.json"); i++) await Task.Delay(50);
            using var metadata = JsonDocument.Parse(File.ReadAllText(file + ".component.json"));
            componentSha256 = metadata.RootElement.GetProperty("sha256").GetString();
            componentPath = metadata.RootElement.GetProperty("path").GetString();
            var componentKind = proxy ? "proxy" : standalone ? "standalone" : "xiaobai";
            var release = File.ReadAllText(Path.Combine(root, "artifacts", $"{componentKind}-{(editorX86 ? "x86" : "x64")}-path.txt")).Trim();
            var expectedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(release, proxy ? "GamePadT9.Xiaobai.dll" : standalone ? "GamePadT9.TextService.dll" : "weasel-gamepad.dll"))));
            if (componentSha256 != expectedHash) throw new Exception("目标编辑器加载的组件不是当前构建版本。");
            if (!isolated)
            {
                using var installed = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "artifacts", componentKind + "-installation.json")));
                var expectedPath = installed.RootElement.EnumerateArray().Single(e => e.GetProperty("architecture").GetString() == (editorX86 ? "x86" : "x64")).GetProperty("destination").GetString();
                if (!string.Equals(componentPath, expectedPath, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("安装验证加载了其他目录的 DLL：" + componentPath);
            }
        }
        overlay.Present(4, 0);
        await Task.Delay(150);
        if (!overlay.Visible || TsfClient.GetForegroundWindow() != hwnd)
            throw new Exception("启用时浮窗未显示或抢走输入焦点。");
        // Exercise the same right-stick + RT event path as the interactive host.
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
        if (overlay.DisplayedPreedit.Replace(" ", "") != "64426") throw new Exception("候选区编码未按顶部 123 的布局显示：" + overlay.DisplayedPreedit);
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
        if (NumericInput.SentKeyEvents != 20 || useTestEditor && BackspaceInput.SentKeyEvents != 0) throw new Exception("完整 TSF 控件发生了不必要的键盘模拟。");
        if (!useTestEditor)
        {
            // Leave the owned scratch document empty before closing its tab.
            for (var i = 0; i < deleted.Length + 1; i++) await session.Handle(new(PadAction.Backspace));
            await Task.Delay(120);
            if (Read(editor).TrimEnd('\r', '\n') != "") throw new Exception("专用记事本验证文档未清空。");
        }
        controller.Update(default, tick++);
        controller.Update(new Gamepad { Buttons = Buttons.B }, tick++);
        tick += 1001;
        foreach (var action in controller.Update(new Gamepad { Buttons = Buttons.B }, tick++)) await session.Handle(action);
        if (overlay.Visible) throw new Exception("关闭输入后九格未隐藏。");
        var inputProfileRestored = inputMethods == null ? null : await inputMethods.RunAsync(inputWindow, "query");
        if (inputMethods != null && (inputMethods.HasSavedProfiles || !inputProfileRestored!.Ok || inputProfileRestored.After != inputProfileBefore!.Before))
            throw new Exception("长按 B 未恢复编辑线程原输入法：" + session.Message);
        var scratchTabClosed = !useTestEditor && await CloseScratchTab(editor);
        var report = new
        {
            time = DateTimeOffset.Now, passed = true, completedAllChecks = backspaceVerified, scratchFile = file,
            xiaobaiProfileUsed = target.Value.Backend == InputBackend.Xiaobai, standaloneProfileUsed = target.Value.Backend == InputBackend.Standalone,
            isolatedComponent = isolated, editorArchitecture = editorX86 ? "x86" : "x64",
            componentSha256, componentPath,
            targetApplication = useTestEditor ? "WPF test editor" : "Notepad",
            targetProcess = target.Value.Process, targetWindow = $"0x{hwnd:X}", before, after,
            automaticInputMethodSwitch = inputMethods != null, frameThread, inputThread = inputWindow.Thread,
            scratchTabClosed,
            crossThreadEditor = inputWindow.Thread != frameThread,
            inputProfileBefore, inputProfileAfter, inputProfileRestored, frameProfileBefore, frameProfileAfter,
            input = "Synthetic XInput state samples -> production Controller -> original installed librime -> production TSF client",
            physicalController = false, t9KeyboardSimulation = false, numericKeyEvents = NumericInput.SentKeyEvents,
            backspaceKeyEvents = BackspaceInput.SentKeyEvents,
            staleTargetRejected = true,
            overlayVisibleWithoutFocusSteal = true, overlayAboveTarget = true, candidatePanelRendered = true,
            candidateHighlightNavigation = true, candidatePaging = true,
            preeditUsesTopRow123 = true,
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
    private async Task<bool> CloseScratchTab(AutomationElement editor)
    {
        try
        {
            if (scratchWindow == null || scratchName == null || !scratchWindow.Current.Name.Contains(scratchName, StringComparison.Ordinal) ||
                Read(editor).TrimEnd('\r', '\n') != "") return false;
            // Recent Notepad versions only expose the tab's close button on hover.
            // Use the accessible File menu instead; this never sends Ctrl+W or a key event.
            var menu = scratchWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "File"));
            if (menu?.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand) != true) return false;
            ((ExpandCollapsePattern)expand).Expand(); await Task.Delay(100);
            var close = scratchWindow.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
                new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "关闭选项卡"), new PropertyCondition(AutomationElement.NameProperty, "Close tab"))));
            if (close?.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke) != true)
            { ((ExpandCollapsePattern)expand).Collapse(); return false; }
            if (!scratchWindow.Current.Name.Contains(scratchName, StringComparison.Ordinal)) return false;
            ((InvokePattern)invoke).Invoke();
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(50);
                if (!scratchWindow.Current.Name.Contains(scratchName, StringComparison.Ordinal)) return true;
                // Only dismiss the save prompt for our known, cleared scratch document.
                var prompt = scratchWindow.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text))
                    .Cast<AutomationElement>().Any(t => t.Current.Name.Contains(Path.Combine(root, "artifacts", scratchName + ".txt"), StringComparison.Ordinal));
                var discard = scratchWindow.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SecondaryButton"));
                if (prompt && discard != null && discard.Current.Name is "不保存" or "Don't save" && discard.TryGetCurrentPattern(InvokePattern.Pattern, out var dismiss))
                    ((InvokePattern)dismiss).Invoke();
            }
            return false;
        }
        catch (ElementNotAvailableException) { return true; }
    }
    private static void CaptureOverlay(MainForm form, string path)
    {
        using var bitmap = form.CreateSnapshot();
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
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
