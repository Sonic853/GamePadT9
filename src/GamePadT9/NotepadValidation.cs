using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GamePadT9;

// Opens an owned, empty scratch document. UI Automation is used only to focus/read it;
// the ONLY text-writing path under test is Rime -> TsfClient -> ITfInsertAtSelection.
internal sealed class NotepadValidation : Form
{
    private readonly string root;
    private readonly RimeEngine engine;
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    public NotepadValidation(string root, RimeEngine engine)
    {
        this.root = root; this.engine = engine;
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        StartPosition = FormStartPosition.Manual; Location = new System.Drawing.Point(-2000, -2000);
        Shown += async (_, _) => { try { await Run(); Result = 0; } catch (Exception ex) { Console.Error.WriteLine(ex); } finally { Close(); } };
    }
    private async Task Run()
    {
        var name = "GamePadT9-TSF-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var file = Path.Combine(root, "artifacts", name + ".txt");
        File.WriteAllText(file, "", new UTF8Encoding(false));
        var start = new ProcessStartInfo("notepad.exe") { UseShellExecute = true };
        start.ArgumentList.Add(file);
        Process.Start(start)?.Dispose();
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
        // Exercise the same right-stick + RT event path as the interactive host.
        var controller = new Controller(); controller.Update(default, 0);
        (short x, short y)[] positions = [(22000, 0), (-22000, 0), (-22000, 0), (0, 22000), (22000, 0)];
        long tick = 10;
        foreach (var (x, y) in positions)
        {
            controller.Update(new Gamepad { RX = x, RY = y }, tick++);
            foreach (var action in controller.Update(new Gamepad { RX = x, RY = y, RT = 200 }, tick++))
                if (action.Action == PadAction.Region) engine.InputRegion(action.Region);
        }
        if (!Validation.Find(engine, "你好")) throw new Exception("原版小白候选中未找到你好。");
        controller.Update(default, tick++);
        foreach (var action in controller.Update(new Gamepad { Buttons = Buttons.A }, tick++))
            if (action.Action == PadAction.Confirm) engine.Confirm();
        if (engine.PendingCommit != "你好") throw new Exception("A 选择后的引擎输出不符。");
        var error = await new TsfClient().Commit(target.Value, engine.PendingCommit);
        if (error != null) throw new Exception(error);
        engine.AcknowledgeCommit();
        await Task.Delay(200);
        var after = Read(editor);
        if (after.TrimEnd('\r', '\n') != "你好") throw new Exception("记事本读取结果不符：" + after);
        var rejected = await new TsfClient().Commit(target.Value with { Epoch = target.Value.Epoch + 1 }, "不应上屏");
        if (rejected == null || Read(editor) != after) throw new Exception("过期焦点令牌保护失败。");
        var report = new
        {
            time = DateTimeOffset.Now, passed = true, scratchFile = file,
            targetProcess = target.Value.Process, targetWindow = $"0x{hwnd:X}", before, after,
            input = "Synthetic XInput state samples -> production Controller -> original installed librime -> production TSF client",
            physicalController = false, keyboardSimulation = false,
            staleTargetRejected = true,
            proof = "TSF edit-session success receipt AND independent UI Automation readback of Notepad document"
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        File.WriteAllText(Path.Combine(root, "artifacts", "notepad-verification.json"), json, new UTF8Encoding(false));
        Console.WriteLine(json);
    }
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
