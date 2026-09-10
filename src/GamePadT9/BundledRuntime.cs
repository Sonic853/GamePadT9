using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamePadT9;

// Immutable, verified engine/data payload; learning and generated indexes stay
// in the writable portable directory and never enter the release manifest.
internal static class BundledRuntime
{
    internal sealed record Manifest(int Format, string EngineVersion, string Schema, Dictionary<string, string> Files);
    internal static bool Enabled(string root)
    {
        var file = Path.Combine(root, "portable.json");
        if (!File.Exists(file)) return false;
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json.RootElement.TryGetProperty("runtime", out var runtime) && runtime.GetString() == "bundled-rime";
    }
    internal static Settings Prepare(string root)
    {
        var payload = Path.Combine(root, "runtime", "rime");
        var file = Path.Combine(payload, "manifest.json");
        if (!File.Exists(file)) throw new FileNotFoundException("内置 Rime 引擎与词库缺失，请重新解压完整独立版便携包。", file);
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (manifest is not { Format: 1, Schema: "xiaobai_simp", Files.Count: > 0 } || string.IsNullOrWhiteSpace(manifest.EngineVersion))
            throw new InvalidDataException("内置 Rime 数据清单无效，请重新解压完整独立版便携包。");
        foreach (var required in new[] { "rime.dll", "data/build/default.yaml", "data/build/xiaobai_simp.schema.yaml", "data/build/xiaobai_simp.table.bin", "data/rime.lua" })
            if (!manifest.Files.ContainsKey(required)) throw new InvalidDataException("内置 Rime 清单缺少文件：" + required);
        foreach (var (relative, expected) in manifest.Files)
        {
            // These are manifest-owned release assets, never arbitrary paths.
            if (!Regex.IsMatch(relative, @"\A(?:rime\.dll|data/(?:rime\.lua|build/[a-z0-9_.]+|opencc/[A-Za-z0-9_.]+)|licenses/[A-Za-z0-9_.-]+)\z") ||
                relative.Contains("..", StringComparison.Ordinal) || !Regex.IsMatch(expected, @"\A[A-Fa-f0-9]{64}\z"))
                throw new InvalidDataException("内置 Rime 清单路径或校验值无效。");
            var path = Path.Combine(payload, relative);
            if (!File.Exists(path) || !string.Equals(PortableRuntime.Hash(path), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("内置 Rime 文件缺失或损坏，请重新解压独立版：" + relative);
        }
        PortableRuntime.RequireMachine(Path.Combine(payload, "rime.dll"), "x86");
        var user = Path.Combine(root, "data", "builtin-user");
        Directory.CreateDirectory(user);
        var lua = Path.Combine(user, "rime.lua");
        if (!File.Exists(lua)) File.Copy(Path.Combine(payload, "data", "rime.lua"), lua);
        return new(payload, manifest.Schema, Path.Combine(payload, "data", "build"), user);
    }
    internal static ProgramProfiles DefaultProfiles => new() { Global = new() { Mode = InputFocusMode.External, Completion = CompletionDestination.Clipboard } };
    internal static void RequireInputComponent(InputBehavior behavior, ComponentState state)
    {
        if (behavior.Mode == InputFocusMode.External && behavior.Completion == CompletionDestination.Clipboard) return;
        if (!state.Standalone && !state.Injected)
            throw new InvalidOperationException("自动填入需要输入法组件。请在设置 → 输入法组件中注册 GamePad T9 或启用小白 T9 联动；也可在程序列表中选择使用外部输入框 → 完成输入后复制到剪切板。");
    }
}
