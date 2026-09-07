using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace GamePadT9;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Length > 0;
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            if (args.Length == 1 && args[0] is "--register" or "--unregister" or "--activate") return Control(args[0][2..]);
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            var root = Settings.FindRoot();
            using var instanceLock = new Mutex(true, "Local\\GamePadT9.Validation.Host", out var first);
            if (!first) throw new InvalidOperationException("GamePad T9 已在运行。请先关闭已有实例。");
            using var engine = new RimeEngine(Settings.Load(root));
            if (args.Contains("--self-test")) return Validation.Run(root, engine);
            if (args.Contains("--verify-notepad") || args.Contains("--verify-editor"))
            {
                using var verification = new NotepadValidation(root, engine, args.Contains("--verify-editor"));
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            using var host = new GamePadApplication(engine);
            System.Windows.Forms.Application.Run(host);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            if (!headless) MessageBox.Show(ex.Message, "GamePad T9", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    internal static int Control(string action)
    {
        var locationFile = Path.Combine(AppContext.BaseDirectory, "bridge-path.txt");
        var bridgeFolder = File.Exists(locationFile) ? File.ReadAllText(locationFile).Trim() : AppContext.BaseDirectory;
        var info = new ProcessStartInfo(Path.Combine(bridgeFolder, "BridgeControl.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(action);
        using var process = Process.Start(info) ?? throw new IOException("无法启动 TSF 注册工具。");
        var output = process.StandardOutput.ReadToEnd(); var error = process.StandardError.ReadToEnd(); process.WaitForExit();
        Console.Write(output); Console.Error.Write(error); return process.ExitCode;
    }
}

internal static class Validation
{
    internal static int Run(string root, RimeEngine engine)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); checks.Add(name); }
        var pad = new Controller();
        Check(pad.Update(new Gamepad { RT = 255, Buttons = Buttons.A }, 0).Count == 0, "Connecting with held buttons does not type");
        pad.Update(default, 10);
        var stroke = pad.Update(new Gamepad { RX = -20000, RY = 20000, RT = 200 }, 20);
        Check(stroke.Count == 1 && stroke[0].Region == 0, "Upper-left region confirmed once");
        Check(pad.Update(new Gamepad { RX = -20000, RY = 20000, RT = 255 }, 30).Count == 0, "Held trigger does not repeat");
        pad.Update(new Gamepad { RX = -11000, RY = 11000 }, 40);
        Check(pad.Region == 0, "Boundary hysteresis prevents region jitter");
        pad.Update(default, 50);
        Check(pad.Region == 4, "Released stick selects center");
        var both = pad.Update(new Gamepad { RT = 200, Buttons = Buttons.R3 }, 60);
        Check(both.Count == 1, "Simultaneous RT and R3 produce one input");
        pad.Update(default, 70);
        pad.Update(new Gamepad { Buttons = Buttons.B }, 80);
        Check(pad.Update(new Gamepad { Buttons = Buttons.B }, 1080).Single().Action == PadAction.Disable, "Long B disables input");
        Check(pad.Update(default, 1090).Count == 0, "Long B release does not also cancel");
        var modeChange = pad.Update(new Gamepad { Buttons = Buttons.Y, RT = 200 }, 1100);
        Check(modeChange.Count == 1 && modeChange[0].Action == PadAction.SwitchMode, "Y mode switch suppresses simultaneous trigger input");
        Check(pad.Update(new Gamepad { Buttons = Buttons.Y, RT = 200 }, 1110).Count == 0, "Held Y does not toggle repeatedly");
        pad.Update(default, 1120);
        var r3 = pad.Update(new Gamepad { Buttons = Buttons.R3 }, 1130);
        Check(r3.Single().StickClick, "R3 remains distinguishable for numeric zero");
        var numericBlocked = false;
        try { NumericInput.SendDigit(InputMode.T9, 5, 0); }
        catch (InvalidOperationException) { numericBlocked = true; }
        Check(numericBlocked && NumericInput.SentKeyEvents == 0, "Keyboard injection is rejected outside numeric mode");
        var devices = Enumerable.Range(0, 4).Where(i => Controller.Read((uint)i, out _)).ToArray();
        // ni hao = 6 4 4 8 6 in the original physical-keypad encoding.
        foreach (var region in new[] { 5, 3, 3, 1, 5 }) engine.InputRegion(region);
        Check(engine.PendingCommit.Length == 0, "Original xiaobai T9 encodes without premature commit");
        Check(engine.View.Candidates.Length > 0, "Original installed schema returns Chinese candidates");
        var firstPage = engine.View;
        var found = Find(engine, "你好");
        Check(found, "Original dictionary contains nihao candidate 你好");
        engine.Confirm();
        Check(engine.PendingCommit == "你好", "Direct candidate selection returns Chinese commit 你好");
        engine.AcknowledgeCommit(); engine.Clear();
        engine.InputRegion(5); engine.Process(0xFF08);
        Check(engine.View.Preedit.Length == 0 && engine.PendingCommit.Length == 0, "Backspace edits engine composition without an OS key event");
        engine.InputRegion(0);
        var symbols = engine.View;
        Check(symbols.Candidates.Length > 0, "Top-left symbol group returns original schema candidates");
        engine.Clear();
        var report = new { time = DateTimeOffset.Now, checks, connectedControllerSlots = devices, firstPage, symbols, tsfIntegration = "Not exercised by --self-test; requires a focused TSF application." };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        File.WriteAllText(Path.Combine(root, "artifacts", "self-test.json"), json, new UTF8Encoding(false));
        Console.WriteLine(json); return 0;
    }
    internal static bool Find(RimeEngine engine, string wanted)
    {
        for (var page = 0; page < 30; page++)
        {
            var index = Array.FindIndex(engine.View.Candidates, c => c.Text == wanted);
            if (index >= 0)
            {
                for (var n = 0; n < 30 && engine.View.Highlight != index; n++) engine.Process(0xFF54);
                return engine.View.Highlight == index;
            }
            if (engine.View.LastPage) break;
            engine.Process(0xFF56);
        }
        return false;
    }
}
