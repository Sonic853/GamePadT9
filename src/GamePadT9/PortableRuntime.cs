using Microsoft.Win32;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GamePadT9;

internal static class PortableRuntime
{
    internal static bool IsPortable(string root) => File.Exists(Path.Combine(root, "portable.json"));
    internal static string ComponentDirectory(string root, string kind, string architecture)
    {
        var bundled = Path.Combine(root, "components", kind, architecture);
        if (Directory.Exists(bundled)) return bundled;
        var pointer = File.ReadAllText(Path.Combine(root, "artifacts", $"{kind}-{architecture}-path.txt")).Trim();
        return Path.GetFullPath(pointer, root);
    }
    // A 32-bit host must inspect the actual 64-bit system DLL, not the WOW64 redirect.
    internal static string FilePath(string path)
    {
        path = Environment.ExpandEnvironmentVariables(path);
        var system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32") + "\\";
        return Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess && path.StartsWith(system, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Sysnative", path[system.Length..]) : path;
    }
    internal static void RequireMachine(string path, string architecture)
    {
        using var stream = File.OpenRead(FilePath(ExpandInstallPath(path, architecture))); using var pe = new PEReader(stream);
        if (pe.PEHeaders.CoffHeader.Machine != (architecture == "x64" ? Machine.Amd64 : Machine.I386))
            throw new InvalidDataException($"组件架构不匹配（需要 {architecture}）：{path}");
    }
    internal static string ExpandInstallPath(string path, string architecture)
    {
        if (architecture == "x64" && Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
        {
            path = path.Replace("%ProgramFiles%", Environment.GetEnvironmentVariable("ProgramW6432"), StringComparison.OrdinalIgnoreCase)
                .Replace("%CommonProgramFiles%", Environment.GetEnvironmentVariable("CommonProgramW6432"), StringComparison.OrdinalIgnoreCase);
        }
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"')));
    }
    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FilePath(path))));
    internal static string? RegistryText(RegistryHive hive, RegistryView view, string key, string name)
    {
        using var registry = RegistryKey.OpenBaseKey(hive, view); using var sub = registry.OpenSubKey(key);
        return sub?.GetValue(name) as string;
    }
    internal static Settings Discover(string root, string? selectedInstall = null, string? selectedUser = null)
    {
        var candidates = new List<string>();
        if (selectedInstall != null) candidates.Add(selectedInstall);
        else
        {
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
            {
                var path = RegistryText(hive, view, @"SOFTWARE\Rime\Weasel", "WeaselRoot");
                if (!string.IsNullOrWhiteSpace(path)) candidates.Add(path);
            }
            foreach (var programFiles in new[] { Environment.GetEnvironmentVariable("ProgramW6432"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) }.OfType<string>().Distinct())
            {
                var rime = Path.Combine(programFiles, "Rime");
                if (Directory.Exists(rime)) candidates.AddRange(Directory.GetDirectories(rime, "*xiaobai*"));
            }
        }
        var install = candidates.Distinct(StringComparer.OrdinalIgnoreCase).Select(p => Environment.ExpandEnvironmentVariables(p.Trim('"')))
            .FirstOrDefault(p => File.Exists(Path.Combine(p, "rime.dll")))
            ?? throw new DirectoryNotFoundException("未检测到小白 T9 的 rime.dll，请选择已安装的小白 T9 文件夹。");
        RequireMachine(Path.Combine(install, "rime.dll"), "x86");
        var user = selectedUser ?? RegistryText(RegistryHive.CurrentUser, RegistryView.Registry32, @"SOFTWARE\Rime\Weasel", "RimeUserDir")
            ?? RegistryText(RegistryHive.CurrentUser, RegistryView.Registry64, @"SOFTWARE\Rime\Weasel", "RimeUserDir")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");
        user = Environment.ExpandEnvironmentVariables(user);
        var build = Path.Combine(user, "build");
        if (!File.Exists(Path.Combine(build, "xiaobai_simp.schema.yaml")) || !File.Exists(Path.Combine(build, "xiaobai_simp.table.bin")))
            throw new DirectoryNotFoundException("未找到已部署的 xiaobai_simp 方案和词库；请先在小白 T9 中完成部署，或选择其用户数据文件夹。");
        // Cache public deployed schemas, tables and Lua only. Never import *.userdb,
        // learning data or another machine's installation paths into a release archive.
        var files = Directory.GetFiles(build).Where(p => Path.GetExtension(p) is ".yaml" or ".bin").Order().ToArray();
        var lua = Directory.Exists(Path.Combine(user, "lua")) ? Directory.GetFiles(Path.Combine(user, "lua"), "*", SearchOption.AllDirectories) : [];
        var script = Path.Combine(user, "rime.lua");
        var sources = files.Concat(lua).Concat(File.Exists(script) ? [script] : []).ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(install + "\n" + Hash(Path.Combine(install, "rime.dll")) + "\n" +
            string.Join("\n", sources.Select(p => $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}")))))[..16];
        var cache = Path.Combine(root, "data", "local", fingerprint);
        var prebuilt = Path.Combine(cache, "prebuilt"); var isolated = Path.Combine(cache, "user");
        if (!File.Exists(Path.Combine(cache, "ready")))
        {
            Directory.CreateDirectory(prebuilt); Directory.CreateDirectory(isolated);
            foreach (var file in files) File.Copy(file, Path.Combine(prebuilt, Path.GetFileName(file)), true);
            foreach (var file in lua.Append(script).Where(File.Exists))
            {
                var destination = Path.Combine(isolated, Path.GetRelativePath(user, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true);
            }
            File.WriteAllText(Path.Combine(cache, "ready"), "Local deployed xiaobai snapshot");
        }
        return new(Path.GetFullPath(install), "xiaobai_simp", prebuilt, isolated);
    }
    internal static Settings Prepare(string root)
    {
        if (BundledRuntime.Enabled(root)) return BundledRuntime.Prepare(root);
        string? install = null, user = null;
        var overrides = Path.Combine(root, "runtime-location.json");
        if (File.Exists(overrides))
        {
            var value = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(overrides));
            install = value?.GetValueOrDefault("install"); user = value?.GetValueOrDefault("user");
        }
        return Discover(root, install, user);
    }
}
