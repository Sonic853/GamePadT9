using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Automation;

namespace GamePadT9;

// Uses a deliberately IME-disabled, owned editor. The clipboard path must work
// with no TSF endpoint, including automatic clipboard fallback at completion.
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
        Check(warning == null && profiles.Global is { Mode: InputFocusMode.External, Completion: CompletionDestination.Target },
            "A fresh independent profile defaults to external editing and target completion");
        var noComponents = new ComponentState(false, false, false);
        BundledRuntime.RequireInputComponent(profiles.Global, noComponents);
        Check(noComponents.CanRegister && !noComponents.CanInject, "No Xiaobai installation is needed for clipboard use or standalone registration");
        foreach (var behavior in new[] { new InputBehavior(), new() { Mode = InputFocusMode.Defocus } })
        {
            var blocked = false;
            try { BundledRuntime.RequireInputComponent(behavior, noComponents); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, behavior.Mode + ": unavailable automatic submission gives a component setup hint");
            BundledRuntime.RequireInputComponent(behavior, new(true, false, false));
            BundledRuntime.RequireInputComponent(behavior, new(false, true, true));
        }
        var saved = profiles with { Global = new() { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard } }; saved.Save(fixture);
        Check(ProgramProfiles.Load(fixture, out _).Global == saved.Global, "An explicitly saved clipboard preference is preserved");
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
        var status = new ComponentState(true, false, false);
        var statusReads = 0;
        input.ComponentStatus = () => { statusReads++; return status; };
        List<string> notices = [];
        input.Error += notices.Add;
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
            var behavior = profiles.Global;
            input.FocusConfiguration = () => (behavior, window!.Value, "independent-owned-editor");
            var originalProfile = (await methods.Switcher.RunAsync(window!.Value, "query")).After;
            await input.Enable(true);
            Check(input.Enabled && input.Focused?.OwnsFocus == true && methods.Switcher.HasSavedProfiles && statusReads == 0,
                "Target completion records the original profile and opens without checking component availability");
            foreach (var region in new[] { 5, 3, 3, 1, 5 }) await input.Handle(new(PadAction.Region, region));
            Check(Validation.Find(engine, "你好"), "The built-in dictionary returns Chinese candidates without Xiaobai");
            await input.Handle(new(PadAction.Confirm));
            await input.Handle(new(PadAction.SwitchMode));
            await input.Handle(new(PadAction.Region, 1)); await input.Handle(new(PadAction.Region, 1, Detail: 0));
            await input.Handle(new(PadAction.Confirm));
            await input.Handle(new(PadAction.SwitchMode));
            await input.Handle(new(PadAction.Region, 0)); await input.Handle(new(PadAction.Region, 4, StickClick: true));
            const string expected = "你好a 10";
            var lastCopied = expected;
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
                status = noComponents; // The completion decision must not use an activation-time snapshot.
                await input.Handle(new(PadAction.Toggle));
                Check(!input.Enabled && Clipboard.GetText() == expected && ((ValuePattern)field.GetCurrentPattern(ValuePattern.Pattern)).Current.Value == "",
                    "View + Menu falls back to copying the draft when components are missing");
                Check(statusReads == 1 && notices.Count == 1 && notices[0].Contains("已复制到剪贴板") && input.Message == notices[0],
                    "Completion checks the current component state once and preserves the tray notification after closing");
                Check(input.Focused!.Draft.Length == 0 && !methods.Switcher.HasSavedProfiles && NumericInput.SentKeyEvents == 0,
                    "Successful fallback clears the completed draft, restores the profile and sends no keyboard events");
                Check((await methods.Switcher.RunAsync(window.Value, "query")).After == originalProfile,
                    "Fallback restores the input profile captured before the external editor opened");

                async Task BeginDraft(string text)
                {
                    SetForegroundWindow(editor.MainWindowHandle); field.SetFocus(); await Task.Delay(80);
                    await input.Enable(true);
                    Check(input.Enabled && input.Focused!.OwnsFocus, "The external editor can reopen for another completion");
                    input.Focused!.Form.Editor.Text = text;
                }
                status = new(false, false, true); // Original Xiaobai alone is not an injected component.
                await BeginDraft(new string('测', 1030));
                Check(statusReads == 1, "Reopening never checks component availability early");
                await input.Handle(new(PadAction.Complete)); lastCopied = new string('测', 1030);
                Check(!input.Enabled && Clipboard.GetText() == lastCopied && statusReads == 2 && notices.Count == 2,
                    "Long-confirm completion falls back with plain Xiaobai installed, even beyond the TSF transaction limit");
                await input.Handle(new(PadAction.Complete));
                Check(statusReads == 2 && notices.Count == 2, "Repeated completion after closure never copies or notifies twice");

                await BeginDraft("保留草稿"); await input.Handle(new(PadAction.Disable));
                Check(!input.Enabled && input.Focused!.Draft == "保留草稿" && Clipboard.GetText() == lastCopied && statusReads == 2 && notices.Count == 2,
                    "Cancellation retains the draft without querying components, copying or notifying");
                behavior = behavior with { Completion = CompletionDestination.Clipboard };
                await BeginDraft("主动复制");
                Check(!methods.Switcher.HasSavedProfiles, "Explicit clipboard completion does not capture a target profile");
                await input.Handle(new(PadAction.Complete)); lastCopied = "主动复制";
                Check(!input.Enabled && Clipboard.GetText() == lastCopied && statusReads == 2 && notices.Count == 2,
                    "An explicit clipboard preference bypasses component detection and emits no fallback warning");

                // The plain editor deliberately has no endpoint. Available components must
                // attempt normal target delivery; other delivery failures retain the draft.
                behavior = profiles.Global;
                foreach (var available in new[] { new ComponentState(true, false, false), new(false, true, true) })
                {
                    status = available; var reads = statusReads;
                    await BeginDraft("目标不支持"); await input.Handle(new(PadAction.Complete));
                    Check(input.Enabled && input.Focused!.Draft == "目标不支持" && statusReads == reads + 1 && Clipboard.GetText() == lastCopied && notices.Count == 2,
                        available.Standalone ? "Registered components use target delivery; an unsupported target retains the draft" :
                            "Injected components use target delivery; an unsupported target never silently copies");
                    await input.Handle(new(PadAction.Disable));
                }
                Check(!methods.Switcher.HasSavedProfiles && NumericInput.SentKeyEvents == 0,
                    "All completion scenarios restore their profiles and generate no simulated keyboard events");
            }
            finally
            {
                if (Clipboard.ContainsText() && Clipboard.GetText() == lastCopied)
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
