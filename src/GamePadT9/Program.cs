using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace GamePadT9;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var headless = args.Length > 0 && !args.Contains("--settings") && !args.Contains("--programs");
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            Console.InputEncoding = new UTF8Encoding(false);
            if (args.Length == 1 && args[0] is "--register" or "--unregister" or "--activate")
                throw new InvalidOperationException("请使用 View + Menu 开启输入并自动切换输入法。联动组件使用 scripts/install-xiaobai.ps1；独立条目使用 scripts/register-standalone.ps1。");
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            var root = Settings.FindRoot();
            if (args.Length == 4 && args[0] == "--manage-component")
                return IntegrationInstaller.ElevatedEntry(root, Enum.Parse<ComponentAction>(args[1]), args[2], args[3]);
            if (args.Length == 2 && args[0] == "--component-action")
            {
                var result = new IntegrationInstaller(root).RequestAsync(Enum.Parse<ComponentAction>(args[1])).GetAwaiter().GetResult();
                Console.WriteLine(JsonSerializer.Serialize(result)); return result.Ok ? 0 : 1;
            }
            if (args.Contains("--verify-components")) return ComponentValidation.Run(root);
            if (args.Contains("--verify-cache")) return CacheValidation.Run(root);
            if (args.Length == 3 && args[0] == "--build-bundled-cache")
            {
                var settings = BundledRuntime.Prepare(Path.GetFullPath(args[1]));
                using var bundledEngine = new RimeEngine(settings);
                var exported = MixedSchema.Locate(settings).CopyTo(Path.GetFullPath(args[2]));
                Console.WriteLine(JsonSerializer.Serialize(new { exported.Id, exported.Fingerprint })); return 0;
            }
            if (args.Length == 2 && args[0] == "--export-mixed-cache")
            {
                var cache = MixedSchema.Locate(Settings.Load(root), Path.Combine(root, "cache", "mixed"));
                var exported = cache.CopyTo(Path.GetFullPath(args[1]));
                Console.WriteLine(JsonSerializer.Serialize(new { exported.Id, exported.Fingerprint })); return 0;
            }
            if (args.Contains("--verify-mixed"))
            {
                // Use a separate user database so this check can run beside the tray host.
                using var validationLock = new Mutex(true, "Local\\GamePadT9.MixedValidation", out var validationFirst);
                if (!validationFirst) throw new InvalidOperationException("混合输入验证已在运行。");
                var validationSettings = MixedValidation.Prepare(root);
                using var validationEngine = new RimeEngine(validationSettings);
                return MixedValidation.Run(root, validationEngine);
            }
            using var instanceLock = new Mutex(true, "Local\\GamePadT9.Validation.Host", out var first);
            if (!first) throw new InvalidOperationException("GamePad T9 已在运行。请先关闭已有实例。");
            Settings runtime;
            try { runtime = Settings.Load(root); }
            catch (Exception ex) when (PortableRuntime.IsPortable(root) && !BundledRuntime.Enabled(root) && !headless)
            {
                using var setup = new RuntimeSetupForm(root, ex.Message);
                if (setup.ShowDialog() != DialogResult.OK || setup.Result == null) return 0;
                runtime = setup.Result;
            }
            Directory.CreateDirectory(Path.Combine(root, "artifacts"));
            using var engine = new RimeEngine(runtime);
            if (args.Contains("--self-test")) return Validation.Run(root, engine);
            if (args.Length == 2 && args[0] == "--verify-independent")
            {
                using var verification = new IndependentValidation(root, engine, args[1]);
                System.Windows.Forms.Application.Run(verification); return verification.Result;
            }
            if (args.Contains("--verify-controllers")) return ControllerValidation.Run(root, engine);
            if (args.Contains("--verify-focus"))
            {
                using var verification = new FocusValidation(root, engine, args.Contains("--x86"));
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            if (args.Contains("--verify-settings"))
            {
                using var verification = new SettingsValidation(root, engine);
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            if (args.Contains("--verify-backspace"))
            {
                using var verification = new BackspaceValidation(root, engine, args.Contains("--x86"), args.Contains("--xiaobai"));
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            if (args.Contains("--verify-input-method"))
            {
                using var verification = new InputMethodValidation(root, engine, args.Contains("--x86"));
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            if (args.Contains("--verify-notepad") || args.Contains("--verify-editor"))
            {
                using var verification = new NotepadValidation(root, engine, args.Contains("--verify-editor"), args.Contains("--isolated"), args.Contains("--x86"), args.Contains("--standalone"), args.Contains("--proxy"));
                System.Windows.Forms.Application.Run(verification);
                return verification.Result;
            }
            using var host = new GamePadApplication(engine, root);
            if (args.Contains("--settings") || args.Contains("--programs") ||
                PortableRuntime.IsPortable(root) && !BundledRuntime.Enabled(root) && new IntegrationInstaller(root).State() is { Standalone: false, Injected: false })
            {
                EventHandler? show = null;
                show = async (_, _) => { System.Windows.Forms.Application.Idle -= show; if (args.Contains("--programs")) await host.OpenProfiles(); else await host.OpenSettings(); };
                System.Windows.Forms.Application.Idle += show;
            }
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
}

internal static class Validation
{
    internal static int Run(string root, RimeEngine engine)
    {
        var checks = new List<string>();
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAILED: " + name); checks.Add(name); }
        EnglishValidation.Run(Check);
        LayoutValidation.Run(Check);
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
        Check(numericBlocked && NumericInput.SentKeyEvents == 0, "NumPad injection is rejected outside numeric mode");
        using var inputDevices = new GamepadDevices(); inputDevices.Poll(null, out _, true);
        var devices = inputDevices.Connected.Select(d => new { d.Instance, d.Name, d.Family }).ToArray();
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
        var report = new { time = DateTimeOffset.Now, checks, connectedControllers = devices, firstPage, symbols, tsfIntegration = "Not exercised by --self-test; requires a focused TSF application." };
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
