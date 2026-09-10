using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace GamePadT9;

internal enum ComponentAction { RegisterStandalone, UnregisterStandalone, InjectXiaobai, RestoreXiaobai }
internal sealed record ComponentState(bool Standalone, bool Injected, bool XiaobaiInstalled)
{
    internal bool CanRegister => !Standalone && !Injected;
    internal bool CanInject => !Standalone && !Injected && XiaobaiInstalled;
    internal string Description => Standalone && Injected ? "检测到旧配置同时启用了两种方式，请先卸载或还原其中一种。" :
        Standalone ? "当前方式：GamePad T9 独立输入法。卸载后可注入小白 T9。" :
        Injected ? "当前方式：小白 T9 联动。还原后可注册独立输入法。" :
        XiaobaiInstalled ? "请选择一种方式；注册与注入互斥，操作需要管理员权限。" : "未检测到完整的小白 T9 输入组件；可以注册独立输入法。";
}
internal sealed record ComponentResult(bool Ok, string Message);
internal sealed record ComRegistration(string Path, RegistryValueKind Kind);
internal interface IComponentRegistry
{
    ComRegistration? Read(string architecture, bool standalone, bool user);
    void WriteXiaobai(string architecture, ComRegistration value);
}
internal sealed class WindowsComponentRegistry : IComponentRegistry
{
    internal const string StandaloneClsid = "{595B67E9-48A3-4C82-B7B1-64E4A35C9D92}";
    internal const string XiaobaiClsid = "{A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A}";
    private static string Key(bool standalone) => @"SOFTWARE\Classes\CLSID\" + (standalone ? StandaloneClsid : XiaobaiClsid) + @"\InprocServer32";
    internal static RegistryView View(string architecture) => architecture == "x64" ? RegistryView.Registry64 : RegistryView.Registry32;
    public ComRegistration? Read(string architecture, bool standalone, bool user)
    {
        using var root = RegistryKey.OpenBaseKey(user ? RegistryHive.CurrentUser : RegistryHive.LocalMachine, View(architecture));
        using var key = root.OpenSubKey(Key(standalone));
        return key?.GetValue("", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string path && path.Length > 0 ? new(path, key.GetValueKind("")) : null;
    }
    public void WriteXiaobai(string architecture, ComRegistration value)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, View(architecture));
        using var key = root.OpenSubKey(Key(false), true) ?? throw new IOException("小白 T9 的注册项已消失，请重新检查安装状态。");
        key.SetValue("", value.Path, value.Kind);
    }
}

// Installation is an explicit settings action. All mutable state is machine-local;
// a copied package never supplies another machine's original registration backup.
internal sealed class IntegrationInstaller
{
    private static readonly string[] Architectures = ["x64", "x86"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private readonly string root, installRoot;
    private readonly IComponentRegistry registry;
    private readonly Action<string, bool> runRegistration;
    private sealed record Entry(string Architecture, string OriginalPath, string ValueKind, string Sha256, string Destination);
    private sealed record Backup(Entry[] Entries);
    internal IntegrationInstaller(string root, IComponentRegistry? registry = null, string? installRoot = null, Action<string, bool>? runRegistration = null)
    {
        this.root = root; this.registry = registry ?? new WindowsComponentRegistry();
        this.installRoot = Path.GetFullPath(installRoot ?? Path.Combine(Environment.GetEnvironmentVariable("ProgramW6432") ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GamePadT9"));
        this.runRegistration = runRegistration ?? Register;
    }
    private string BackupFile => Path.Combine(installRoot, "components", "original-registration.json");
    internal bool UsesBundledEngine => BundledRuntime.Enabled(root);
    private bool Managed(string path) => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).StartsWith(installRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal ComponentState State()
    {
        var standalone = Architectures.Any(a => registry.Read(a, true, true) != null || registry.Read(a, true, false) != null);
        var values = Architectures.Select(a => registry.Read(a, false, true) ?? registry.Read(a, false, false)).ToArray();
        return new(standalone, values.Any(v => v != null && Managed(v.Path)), values.All(v => v != null));
    }
    internal void Apply(ComponentAction action)
    {
        if (registry is WindowsComponentRegistry && !IsAdministrator()) throw new UnauthorizedAccessException("输入组件操作需要管理员权限。");
        if (!Environment.Is64BitOperatingSystem) throw new PlatformNotSupportedException("此发行包支持 x64 Windows 的 x64/x86 目标程序。");
        var state = State();
        switch (action)
        {
            case ComponentAction.RegisterStandalone:
                if (!state.CanRegister) throw new InvalidOperationException("请先还原小白 T9 或卸载已注册的独立输入法。");
                InstallStandalone(); break;
            case ComponentAction.UnregisterStandalone: RemoveStandalone(); break;
            case ComponentAction.InjectXiaobai:
                if (!state.CanInject) throw new InvalidOperationException("请先卸载 GamePad T9 独立输入法，并确认小白 T9 已安装；已有注入请先还原。");
                Inject(); break;
            case ComponentAction.RestoreXiaobai: Restore(); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
        state = State();
        if (state.Standalone && state.Injected) throw new InvalidOperationException("检测到互斥方式冲突，请在设置中还原其中一种。");
    }
    private string Payload(string kind, string architecture, string name)
    {
        var folder = PortableRuntime.ComponentDirectory(root, kind, architecture);
        var file = Path.Combine(folder, name); PortableRuntime.RequireMachine(file, architecture);
        if (PortableRuntime.IsPortable(root))
        {
            var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(root, "components", "sha256.json")), Json)
                ?? throw new InvalidDataException("组件清单缺失。");
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!hashes.TryGetValue(relative, out var expected) || !string.Equals(expected, PortableRuntime.Hash(file), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发行包组件校验失败，请重新解压完整发行包：" + relative);
        }
        return file;
    }
    private static void CopyVerified(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!File.Exists(destination)) File.Copy(PortableRuntime.FilePath(source), destination);
        if (PortableRuntime.Hash(source) != PortableRuntime.Hash(destination)) throw new IOException("安装目录中的组件校验失败：" + destination);
    }
    private void InstallStandalone()
    {
        var plan = Architectures.Select(a => (Architecture: a, Dll: Payload("standalone", a, "GamePadT9.TextService.dll"), Control: Payload("standalone", a, "BridgeControl.exe"))).ToArray();
        var staged = new List<string>();
        foreach (var item in plan)
        {
            var folder = Path.Combine(installRoot, "standalone", item.Architecture + "-" + PortableRuntime.Hash(item.Dll)[..16]);
            CopyVerified(item.Dll, Path.Combine(folder, "GamePadT9.TextService.dll"));
            var control = Path.Combine(folder, "BridgeControl.exe"); CopyVerified(item.Control, control); staged.Add(control);
        }
        var changed = new List<string>();
        try
        {
            foreach (var control in staged) { changed.Add(control); runRegistration(control, true); }
            if (!Architectures.All(a => registry.Read(a, true, true) != null)) throw new IOException("独立输入法注册结果不完整。");
        }
        catch (Exception failure)
        {
            var errors = new List<string> { failure.Message };
            foreach (var control in changed.AsEnumerable().Reverse())
                try { runRegistration(control, false); } catch (Exception ex) { errors.Add("回滚失败：" + ex.Message); }
            throw new IOException(string.Join("；", errors), failure);
        }
    }
    private void RemoveStandalone()
    {
        var failures = new List<string>();
        foreach (var a in Architectures)
        {
            if (registry.Read(a, true, true) == null && registry.Read(a, true, false) == null) continue;
            try
            {
                // Use a verified bundled helper; do not execute a registry-supplied EXE.
                var dll = Payload("standalone", a, "GamePadT9.TextService.dll");
                var helper = Payload("standalone", a, "BridgeControl.exe");
                var folder = Path.Combine(installRoot, "standalone", a + "-" + PortableRuntime.Hash(dll)[..16]);
                CopyVerified(dll, Path.Combine(folder, "GamePadT9.TextService.dll")); CopyVerified(helper, Path.Combine(folder, "BridgeControl.exe"));
                runRegistration(Path.Combine(folder, "BridgeControl.exe"), false);
                if (registry is WindowsComponentRegistry)
                {
                    using var current = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, WindowsComponentRegistry.View(a));
                    current.DeleteSubKeyTree(@"SOFTWARE\Microsoft\CTF\TIP\" + WindowsComponentRegistry.StandaloneClsid, false);
                }
            }
            catch (Exception ex) { failures.Add(a + ": " + ex.Message); }
        }
        if (State().Standalone) failures.Add("注册项仍存在；请保留文件并重试卸载。");
        if (failures.Count > 0) throw new IOException(string.Join("；", failures));
    }
    private void Inject()
    {
        var plan = new List<Entry>();
        foreach (var a in Architectures)
        {
            if (registry.Read(a, false, true) != null) throw new InvalidOperationException("存在用户级小白 COM 覆盖，请先使用其安装程序还原后重试。");
            var current = registry.Read(a, false, false) ?? throw new InvalidOperationException($"未安装 {a} 小白 T9。");
            var path = PortableRuntime.ExpandInstallPath(current.Path, a); PortableRuntime.RequireMachine(path, a);
            var source = Payload("proxy", a, "GamePadT9.Xiaobai.dll");
            var hash = PortableRuntime.Hash(path);
            var folder = Path.Combine(installRoot, "components", a + "-" + PortableRuntime.Hash(source)[..16] + "-" + hash[..16]);
            plan.Add(new(a, current.Path, current.Kind.ToString(), hash, Path.Combine(folder, "GamePadT9.Xiaobai.dll")));
        }
        // Stage both extensions and backup original registrations before changing either.
        foreach (var entry in plan)
        {
            CopyVerified(Payload("proxy", entry.Architecture, "GamePadT9.Xiaobai.dll"), entry.Destination);
            var original = PortableRuntime.ExpandInstallPath(entry.OriginalPath, entry.Architecture);
            if (original.Contains('\r') || original.Contains('\n')) throw new InvalidDataException("小白安装路径包含无效字符。");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(entry.Destination)!, "original.ini"), "[Original]\r\nPath=" + original + "\r\n", Encoding.Unicode);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
        File.WriteAllText(BackupFile + ".tmp", JsonSerializer.Serialize(new Backup(plan.ToArray()), Json)); File.Move(BackupFile + ".tmp", BackupFile, true);
        ChangeRegistrations(plan.Select(e => (e.Architecture, Before: new ComRegistration(e.OriginalPath, Enum.Parse<RegistryValueKind>(e.ValueKind)), After: new ComRegistration(e.Destination, RegistryValueKind.String))).ToArray());
    }
    private void Restore()
    {
        if (!State().Injected) return;
        var backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupFile), Json) ?? throw new IOException("本机小白注册备份缺失。");
        if (backup.Entries.Length != 2 || !backup.Entries.Select(e => e.Architecture).Order().SequenceEqual(Architectures.Order())) throw new IOException("本机小白注册备份不完整。");
        var changes = new List<(string Architecture, ComRegistration Before, ComRegistration After)>();
        foreach (var entry in backup.Entries)
        {
            var current = registry.Read(entry.Architecture, false, false);
            // A vendor update may have already installed a new path. Leave it intact.
            if (current == null || !Managed(current.Path)) continue;
            if (Managed(entry.OriginalPath)) throw new IOException("备份指向扩展自身，无法还原。");
            PortableRuntime.RequireMachine(entry.OriginalPath, entry.Architecture);
            changes.Add((entry.Architecture, current, new(entry.OriginalPath, Enum.Parse<RegistryValueKind>(entry.ValueKind))));
        }
        ChangeRegistrations(changes.ToArray());
        if (State().Injected) throw new IOException("仍有小白注入项未还原，请保留本机备份。");
    }
    private void ChangeRegistrations((string Architecture, ComRegistration Before, ComRegistration After)[] changes)
    {
        var applied = new List<(string Architecture, ComRegistration Before, ComRegistration After)>();
        try
        {
            foreach (var item in changes)
            {
                if (registry.Read(item.Architecture, false, false) != item.Before) throw new IOException("小白注册项已被其他程序更改，已停止操作。");
                applied.Add(item); registry.WriteXiaobai(item.Architecture, item.After);
                if (registry.Read(item.Architecture, false, false) != item.After) throw new IOException("无法核对小白注册项变更。");
            }
        }
        catch (Exception failure)
        {
            var errors = new List<string> { failure.Message };
            foreach (var item in applied.AsEnumerable().Reverse())
                try { if (registry.Read(item.Architecture, false, false) == item.After) registry.WriteXiaobai(item.Architecture, item.Before); }
                catch (Exception ex) { errors.Add("回滚失败：" + ex.Message); }
            throw new IOException(string.Join("；", errors), failure);
        }
    }
    private static void Register(string helper, bool register)
    {
        using var process = Process.Start(new ProcessStartInfo(helper, register ? "register" : "unregister")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }) ?? throw new IOException("无法启动注册组件。");
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit(); Task.WaitAll(output, error);
        if (process.ExitCode != 0) throw new IOException(output.Result + error.Result);
    }
    private static bool IsAdministrator() => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    internal static int ElevatedEntry(string root, ComponentAction action, string resultPath, string expectedUser)
    {
        ComponentResult result;
        try
        {
            if (WindowsIdentity.GetCurrent().User?.Value != expectedUser) throw new UnauthorizedAccessException("请使用当前登录用户的管理员授权，避免把输入法注册到其他账户。");
            using var mutex = new Mutex(false, "Global\\GamePadT9.ComponentSetup");
            var acquired = false;
            try
            {
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("另一项输入组件操作正在运行。");
                new IntegrationInstaller(root).Apply(action);
                result = new(true, "操作完成。请重新打开需要使用输入法的目标程序；Windows 列表未刷新时请注销后登录。");
            }
            finally { if (acquired) mutex.ReleaseMutex(); }
        }
        catch (Exception ex) { result = new(false, ex.Message); }
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        // Never accept an arbitrary elevated output path.
        var full = Path.GetFullPath(resultPath);
        if (!full.StartsWith(Path.Combine(root, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(full) != ".json") return 2;
        File.WriteAllText(full, JsonSerializer.Serialize(result, Json));
        return result.Ok ? 0 : 1;
    }
    internal async Task<ComponentResult> RequestAsync(ComponentAction action)
    {
        // Resolve and verify the request before displaying UAC; the elevated process
        // checks again under the setup mutex immediately before applying changes.
        var state = State();
        if (action == ComponentAction.RegisterStandalone && !state.CanRegister || action == ComponentAction.InjectXiaobai && !state.CanInject)
            return new(false, "安装状态已变化，请先还原/卸载另一种方式。");
        foreach (var a in Architectures)
        {
            if (action is ComponentAction.RegisterStandalone or ComponentAction.UnregisterStandalone)
            { Payload("standalone", a, "GamePadT9.TextService.dll"); Payload("standalone", a, "BridgeControl.exe"); }
            if (action == ComponentAction.InjectXiaobai)
            {
                Payload("proxy", a, "GamePadT9.Xiaobai.dll");
                if (registry.Read(a, false, true) != null) return new(false, "存在用户级小白 COM 覆盖，请先使用其安装程序还原。");
                PortableRuntime.RequireMachine(registry.Read(a, false, false)!.Path, a);
            }
        }
        if (action == ComponentAction.RestoreXiaobai && state.Injected && !File.Exists(BackupFile)) return new(false, "缺少此电脑的小白原注册备份，请修复小白安装后重试。");
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));
        var resultPath = Path.Combine(root, "artifacts", "component-action-" + Guid.NewGuid().ToString("N") + ".json");
        var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root };
        foreach (var value in new[] { "--manage-component", action.ToString(), resultPath, WindowsIdentity.GetCurrent().User!.Value }) info.ArgumentList.Add(value);
        try
        {
            using var process = Process.Start(info) ?? throw new IOException("无法启动组件管理程序。");
            await process.WaitForExitAsync();
            if (!File.Exists(resultPath)) return new(false, "组件操作未完成，请重试。");
            return JsonSerializer.Deserialize<ComponentResult>(File.ReadAllText(resultPath), Json) ?? new(false, "无法读取组件操作结果。");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { return new(false, "已取消管理员授权，未更改输入组件。"); }
    }
}
