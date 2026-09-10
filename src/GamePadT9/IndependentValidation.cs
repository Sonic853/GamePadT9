using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace GamePadT9;

// Uses a deliberately IME-disabled, owned editor. The clipboard path must work
// with no TSF endpoint and without querying/switching a target input profile.
internal sealed class IndependentValidation : Form
{
    private sealed class EmptyRegistry : IComponentRegistry
    {
        public ComRegistration? Read(string architecture, bool standalone, bool user) => null;
        public void WriteXiaobai(string architecture, ComRegistration value) => throw new Exception("Validation must never register or inject.");
    }
    private readonly List<string> checks = [];
    internal int Result { get; private set; } = 1;
    internal IndependentValidation(string root, RimeEngine engine, string editorPath)
    {
        ShowInTaskbar = false; Opacity = 0; Width = Height = 1;
        Shown += async (_, _) =>
        {
            string? error = null;
            try { await Run(root, engine, editorPath); Result = 0; }
            catch (Exception ex) { error = ex.ToString(); Console.Error.WriteLine(ex); }
            var report = JsonSerializer.Serialize(new { passed = Result == 0, checks, error }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(root, "artifacts", "independent-verification.json"), report);
            Console.WriteLine(report); Close();
        };
    }
    private void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks.Add(message); }
    private async Task Run(string root, RimeEngine engine, string editorPath)
    {
        Check(BundledRuntime.Enabled(root), "The independent edition uses the bundled runtime mode");
        var runtime = Settings.Load(root);
        Check(runtime.InstallRoot == Path.Combine(root, "runtime", "rime") && runtime.PrebuiltPath.StartsWith(root, StringComparison.OrdinalIgnoreCase),
            "Engine and dictionary paths remain within this portable package");
        Check(MixedSchema.Locate(runtime).Ready, "The bundled engine has a usable mixed spelling index");
        var fixture = Path.Combine(root, "artifacts", "independent-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixture);
        File.WriteAllText(Path.Combine(fixture, "portable.json"), "{\"runtime\":\"bundled-rime\"}");
        var profiles = ProgramProfiles.Load(fixture, out var warning);
        Check(warning == null && profiles.Global is { Mode: InputFocusMode.External, Completion: CompletionDestination.Clipboard },
            "A fresh independent profile defaults to external editing and clipboard completion");
        var noComponents = new ComponentState(false, false, false);
        BundledRuntime.RequireInputComponent(profiles.Global, noComponents);
        Check(noComponents.CanRegister && !noComponents.CanInject, "No Xiaobai installation is needed for clipboard use or standalone registration");
        foreach (var behavior in new[] { new InputBehavior(), new() { Mode = InputFocusMode.Defocus }, new() { Mode = InputFocusMode.External } })
        {
            var blocked = false;
            try { BundledRuntime.RequireInputComponent(behavior, noComponents); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, behavior.Mode + ": unavailable automatic submission gives a component setup hint");
            BundledRuntime.RequireInputComponent(behavior, new(true, false, false));
            BundledRuntime.RequireInputComponent(behavior, new(false, true, true));
        }
        var saved = profiles with { Global = new() { Mode = InputFocusMode.Defocus } }; saved.Save(fixture);
        Check(ProgramProfiles.Load(fixture, out _).Global == saved.Global, "Existing completion preferences are not silently changed to clipboard");
        File.Delete(Path.Combine(fixture, "program-profiles.json"));
        File.WriteAllText(Path.Combine(fixture, "portable.json"), "{\"runtime\":\"auto-detect-installed-xiaobai\"}");
        Check(!BundledRuntime.Enabled(fixture) && ProgramProfiles.Load(fixture, out _).Global.Completion == CompletionDestination.Target,
            "The original portable edition retains its target-completion default");
        using (var settings = new SettingsForm(new(), _ => { }, integration: new(root, new EmptyRegistry(), Path.Combine(fixture, "install"))))
        {
            settings.Show(); settings.ShowPage(2); await Task.Delay(120);
            Check(settings.IsVisible && settings.Control<Wpf.Ui.Controls.Button>("ToggleStandalone").IsEnabled && !settings.Control<Wpf.Ui.Controls.Button>("ToggleXiaobai").IsEnabled,
                "Settings opens without components and offers standalone registration with optional Xiaobai disabled");
            settings.Close();
        }
        var start = new ProcessStartInfo(Path.GetFullPath(editorPath)) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("GamePadT9 independent validation"); start.ArgumentList.Add("--plain");
        using var editor = Process.Start(start) ?? throw new Exception("Owned editor failed to start.");
        using var methods = new ProfileScope(root);
        using var input = new InputSession(engine, methods.Switcher);
        using var overlay = new MainForm(input);
        input.Changed += () => overlay.Present(4, 1);
        input.OverlayBounds = () => overlay.Bounds;
        try
        {
            AutomationElement? field = null;
            for (var i = 0; i < 80 && field == null; i++)
            {
                await Task.Delay(50); editor.Refresh(); if (editor.HasExited) throw new Exception("Owned editor exited.");
                if (editor.MainWindowHandle != 0)
                    field = AutomationElement.FromHandle(editor.MainWindowHandle).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            }
            if (field == null) throw new Exception("Owned editor missing.");
            InputWindow? window = null;
            for (var i = 0; i < 20; i++)
            {
                SetForegroundWindow(editor.MainWindowHandle); field.SetFocus(); await Task.Delay(80);
                window = InputMethodSwitcher.Foreground(); if (window?.Process == editor.Id) break;
            }
            Check(window?.Process == editor.Id && TsfClient.FindTarget() == null, "The owned plain editor has focus and no gamepad TSF endpoint");
            input.FocusConfiguration = () => (BundledRuntime.DefaultProfiles.Global, window!.Value, "independent-owned-editor");
            await input.Enable(true);
            Check(input.Enabled && input.Focused?.OwnsFocus == true && !methods.Switcher.HasSavedProfiles,
                "External clipboard input opens without activating or recording any input method");
            foreach (var region in new[] { 5, 3, 3, 1, 5 }) await input.Handle(new(PadAction.Region, region));
            Check(Validation.Find(engine, "你好"), "The built-in dictionary returns Chinese candidates without Xiaobai");
            await input.Handle(new(PadAction.Confirm));
            await input.Handle(new(PadAction.SwitchMode));
            await input.Handle(new(PadAction.Region, 1)); await input.Handle(new(PadAction.Region, 1, Detail: 0));
            await input.Handle(new(PadAction.Confirm));
            await input.Handle(new(PadAction.SwitchMode));
            await input.Handle(new(PadAction.Region, 0)); await input.Handle(new(PadAction.Region, 4, StickClick: true));
            const string expected = "你好a 10";
            Check(input.Focused!.Draft == expected, "Chinese, English, space and numeric input edit the local draft");
            var oldClipboard = Clipboard.GetDataObject(); var snapshot = new DataObject();
            if (oldClipboard != null) foreach (var format in oldClipboard.GetFormats(false))
            {
                var data = oldClipboard.GetData(format, false);
                if (data is MemoryStream stream) data = new MemoryStream(stream.ToArray());
                if (data != null) snapshot.SetData(format, false, data);
            }
            try
            {
                await input.Handle(new(PadAction.Toggle));
                Check(!input.Enabled && Clipboard.GetText() == expected && ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "",
                    "View + Menu finishes by copying the draft without typing into the target");
                Check(!methods.Switcher.HasSavedProfiles && NumericInput.SentKeyEvents == 0, "Clipboard-only operation never switches input methods or simulates keyboard events");
            }
            finally
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == expected)
                {
                    // Yield to the STA message pump between OLE clipboard retries.
                    for (var attempt = 0; ; attempt++)
                    {
                        try { if (oldClipboard == null) Clipboard.Clear(); else Clipboard.SetDataObject(snapshot, true, 0, 0); break; }
                        catch (ExternalException) when (attempt < 10) { await Task.Delay(80); }
                    }
                }
            }
        }
        finally { await input.ShutdownAsync(); if (!editor.HasExited) { editor.CloseMainWindow(); await editor.WaitForExitAsync(); } }
    }
    // Keep restoration local even if an assertion fails during a future extension.
    private sealed class ProfileScope(string root) : IDisposable
    {
        internal InputMethodSwitcher Switcher { get; } = new(root);
        public void Dispose() { if (Switcher.HasSavedProfiles) Switcher.RestoreAsync().GetAwaiter().GetResult(); }
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
}
