using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using System.Windows.Forms;

namespace GamePadT9;

internal sealed class BackspaceValidation : Form
{
    private readonly string root;
    private readonly RimeEngine engine;
    private readonly bool x86, xiaobai;
    internal int Result { get; private set; } = 1;
    protected override bool ShowWithoutActivation => true;
    internal BackspaceValidation(string root, RimeEngine engine, bool x86, bool xiaobai)
    {
        this.root = root; this.engine = engine; this.x86 = x86; this.xiaobai = xiaobai;
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        Shown += async (_, _) => { await Run(); Close(); };
    }
    private async Task Run()
    {
        Process? process = null;
        var session = new InputSession(engine);
        var checks = new List<string>();
        string? error = null;
        void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks.Add(description); }
        try
        {
            var scratch = Path.Combine(root, "artifacts", "Backspace-" + Guid.NewGuid().ToString("N"));
            var info = new ProcessStartInfo(Path.Combine(root, "artifacts", "backspace-editor-" + (x86 ? "x86" : "x64"), "BackspaceEditor.exe")) { UseShellExecute = false };
            info.ArgumentList.Add(scratch); if (xiaobai) info.ArgumentList.Add("--xiaobai");
            process = Process.Start(info) ?? throw new IOException("Test editor did not start.");
            for (var i = 0; i < 100 && !File.Exists(scratch + ".ready"); i++) await Task.Delay(50);
            process.Refresh(); var hwnd = process.MainWindowHandle;
            var window = AutomationElement.FromHandle(hwnd);
            var field = window.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)) ?? throw new Exception("Test field missing.");
            field.SetFocus();
            string Read() => ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
            async Task Expect(string text)
            {
                for (var i = 0; i < 50; i++) { if (Read() == text) return; await Task.Delay(25); }
                throw new Exception("Unexpected document: " + Read() + " / " + session.Message);
            }
            Target? target = null;
            for (var i = 0; i < 80; i++) { target = TsfClient.FindTarget(); if (target?.Foreground == hwnd) break; await Task.Delay(25); }
            Check(target?.Foreground == hwnd && target.Value.Backend == (xiaobai ? InputBackend.Xiaobai : InputBackend.Standalone), "The real EDIT control is connected to the requested TSF backend");
            Check(TsfClient.SupportsBackspace(target!.Value), "The component advertises TSF backspace; fallback must follow an unsupported edit receipt");
            await Expect("甲乙ABC"); await session.Enable(true);
            await session.Handle(new(PadAction.Region, 5)); await session.Handle(new(PadAction.Backspace));
            Check(engine.View.Preedit.Length == 0 && Read() == "甲乙ABC" && BackspaceInput.SentKeyEvents == 0, "X removes pending pinyin without sending an OS key or changing the document");
            await session.Handle(new(PadAction.Backspace)); await Expect("甲乙AB");
            Check(BackspaceInput.SentKeyEvents == 2, "Unsupported TSF backspace falls back to exactly one Backspace down/up pair");
            var fresh = TsfClient.FindTarget() ?? throw new Exception("Target disappeared.");
            var rejected = await new TsfClient().Backspace(fresh with { Epoch = fresh.Epoch + 1 });
            Check(rejected != null && BackspaceInput.SentKeyEvents == 2 && Read() == "甲乙AB", "Stale focus refuses Backspace without deleting text or sending keys");
            await session.Handle(new(PadAction.Backspace)); await Expect("甲乙A");
            Check(BackspaceInput.SentKeyEvents == 4, "A second X deletes one character without a duplicate TSF deletion");
            await session.Handle(new(PadAction.SwitchMode)); await session.Handle(new(PadAction.Backspace)); await Expect("甲乙");
            Check(BackspaceInput.SentKeyEvents == 6 && NumericInput.SentKeyEvents == 0, "Numeric mode also uses the same Backspace compatibility fallback");
            Result = 0;
        }
        catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
        finally
        {
            await session.ShutdownAsync();
            if (process != null) { if (!process.HasExited) process.CloseMainWindow(); process.Dispose(); }
            var report = JsonSerializer.Serialize(new { time = DateTimeOffset.Now, passed = Result == 0,
                architecture = x86 ? "x86" : "x64", backend = xiaobai ? "xiaobai" : "standalone", checks,
                backspaceKeyEvents = BackspaceInput.SentKeyEvents, error }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(root, "artifacts", $"backspace-{(xiaobai ? "xiaobai" : "standalone")}-{(x86 ? "x86" : "x64")}-verification.json"), report);
            Console.WriteLine(report);
        }
    }
}
