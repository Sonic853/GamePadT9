using Microsoft.Win32;
using System.Text.Json;

namespace GamePadT9;

internal static class ComponentValidation
{
    private sealed class RegistryFixture : IComponentRegistry
    {
        internal readonly Dictionary<(string, bool, bool), ComRegistration> Values = [];
        internal string? FailWrite;
        public ComRegistration? Read(string a, bool standalone, bool user) => Values.GetValueOrDefault((a, standalone, user));
        public void WriteXiaobai(string a, ComRegistration value)
        {
            Values[(a, false, false)] = value;
            if (FailWrite == a) { FailWrite = null; throw new IOException("Injected registry-write failure"); }
        }
    }
    internal static int Run(string root)
    {
        var checks = new List<string>();
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks.Add(message); }
        void Rejected(Action action, string message) { try { action(); } catch (InvalidOperationException) { Check(true, message); return; } throw new Exception(message); }
        var folder = Path.GetFullPath(Path.Combine(root, "artifacts", "component-test-" + Guid.NewGuid().ToString("N")));
        var registry = new RegistryFixture();
        var originals = new Dictionary<string, ComRegistration>();
        string? failRegistration = null;
        var install = Path.Combine(folder, "Program Files", "GamePadT9");
        void Register(string helper, bool enabled)
        {
            var arch = new DirectoryInfo(Path.GetDirectoryName(helper)!).Name.StartsWith("x64-") ? "x64" : "x86";
            if (enabled) registry.Values[(arch, true, true)] = new(Path.Combine(Path.GetDirectoryName(helper)!, "GamePadT9.TextService.dll"), RegistryValueKind.String);
            else registry.Values.Remove((arch, true, true));
            if (enabled && failRegistration == arch) throw new IOException("Injected second-architecture registration failure");
        }
        string? error = null;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var a in new[] { "x64", "x86" })
            {
                var original = Path.Combine(folder, "Vendor", "xiaobait9-different-version", a, "vendor.dll"); Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                File.Copy(PortableRuntime.FilePath(Path.Combine(windows, a == "x64" ? "System32" : "SysWOW64", "weasel.dll")), original);
                originals[a] = new(original, a == "x64" ? RegistryValueKind.String : RegistryValueKind.ExpandString);
                registry.Values[(a, false, false)] = originals[a];
            }
            var manager = new IntegrationInstaller(root, registry, install, Register);
            Check(manager.State().CanRegister && manager.State().CanInject, "Clean machine offers exactly the two alternative installation paths");
            registry.FailWrite = "x86";
            try { manager.Apply(ComponentAction.InjectXiaobai); throw new Exception("Missing injected failure"); } catch (IOException) { }
            Check(originals.All(p => registry.Read(p.Key, false, false) == p.Value), "Failed second-architecture injection rolls both registrations back, including a write that threw after mutation");
            manager.Apply(ComponentAction.InjectXiaobai);
            Check(manager.State() is { Injected: true, Standalone: false, CanRegister: false }, "Injection enables only xiaobai and prevents standalone registration");
            Check(originals.All(p => File.Exists(p.Value.Path)), "Vendor DLL files remain in their original directories");
            Rejected(() => manager.Apply(ComponentAction.RegisterStandalone), "Backend rejects conflicting standalone registration independently of the UI");
            using (var settings = new SettingsForm(new(), _ => { }, integration: manager))
            {
                var standalone = (Button)settings.Controls.Find("ToggleStandalone", true).Single();
                var xiaobai = (Button)settings.Controls.Find("ToggleXiaobai", true).Single();
                Check(!standalone.Enabled && xiaobai.Enabled && xiaobai.Text == "还原小白 T9 输入", "Settings offers restore and disables registration while xiaobai is injected");
            }
            registry.FailWrite = "x86";
            try { manager.Apply(ComponentAction.RestoreXiaobai); throw new Exception("Missing restore failure"); } catch (IOException) { }
            Check(new[] { "x64", "x86" }.All(a => registry.Read(a, false, false)!.Path.StartsWith(install)), "Partial restore failure rolls back both architectures so restore can be retried");
            manager.Apply(ComponentAction.RestoreXiaobai);
            Check(originals.All(p => registry.Read(p.Key, false, false) == p.Value), "Restore recovers this machine's exact paths and registry value kinds");
            manager.Apply(ComponentAction.InjectXiaobai);
            var updatedPath = Path.Combine(folder, "Vendor", "new-version.dll"); File.Copy(originals["x64"].Path, updatedPath);
            registry.Values[("x64", false, false)] = new(updatedPath, RegistryValueKind.String);
            manager.Apply(ComponentAction.RestoreXiaobai);
            Check(registry.Read("x64", false, false)!.Path == updatedPath && registry.Read("x86", false, false) == originals["x86"], "Restore preserves a newer vendor registration instead of overwriting it with an old backup");
            failRegistration = "x86";
            try { manager.Apply(ComponentAction.RegisterStandalone); throw new Exception("Missing registration failure"); } catch (IOException) { }
            Check(!manager.State().Standalone, "Partial standalone registration is rolled back for both architectures");
            failRegistration = null; manager.Apply(ComponentAction.RegisterStandalone);
            Check(manager.State() is { Standalone: true, Injected: false, CanInject: false }, "Standalone registration excludes xiaobai injection");
            Rejected(() => manager.Apply(ComponentAction.InjectXiaobai), "Backend rejects conflicting xiaobai injection independently of the UI");
            using (var settings = new SettingsForm(new(), _ => { }, integration: manager))
            {
                Check(((Button)settings.Controls.Find("ToggleStandalone", true).Single()).Text == "卸载 GamePad T9 输入法" &&
                    !((Button)settings.Controls.Find("ToggleXiaobai", true).Single()).Enabled, "Settings offers uninstall and disables injection while standalone is registered");
            }
            manager.Apply(ComponentAction.UnregisterStandalone);
            Check(!manager.State().Standalone && !manager.State().Injected, "Uninstall returns to an unmodified, mutually exclusive state");
            // Discovery reads target-machine data and writes only into the new package root.
            var settingsRoot = Path.Combine(folder, "Copied package with spaces"); Directory.CreateDirectory(settingsRoot);
            var runtime = PortableRuntime.Discover(settingsRoot);
            Check(runtime.PrebuiltPath.StartsWith(settingsRoot) && runtime.UserPath.StartsWith(settingsRoot) && File.Exists(Path.Combine(runtime.PrebuiltPath, "xiaobai_simp.schema.yaml")),
                "A relocated package discovers the installed version and creates local schema paths");
            Check(!Directory.GetFileSystemEntries(runtime.UserPath, "*.userdb*", SearchOption.AllDirectories).Any(), "Preparation does not import the user's learned dictionary");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            var expectedParent = Path.GetFullPath(Path.Combine(root, "artifacts")) + Path.DirectorySeparatorChar;
            if (Directory.Exists(folder) && folder.StartsWith(expectedParent, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(folder).StartsWith("component-test-")) Directory.Delete(folder, true);
        }
        var json = JsonSerializer.Serialize(new { passed = error == null, checks, error, scope = "Temporary filesystem and registry fixture; no installed input methods were changed" }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artifacts", "component-verification.json"), json); Console.WriteLine(json);
        return error == null ? 0 : 1;
    }
}
