using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GamePadT9;

// Compile only a private spelling prism. The installed schema, dictionary and
// Windows input-method registrations are never deployed or overwritten here.
internal sealed record MixedSchema(string Id, string Source, string Build, string SchemaFile, string Fingerprint)
{
    private sealed record CacheManifest(string Id, string SchemaHash, string PrismHash);
    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static string NormalizeSchema(string path)
    {
        var text = File.ReadAllText(path).Replace("\r\n", "\n");
        // Deployment timestamps and the installing Rime version do not alter
        // spelling. Preserve all actual options, including custom bindings.
        return Regex.Replace(text, @"(?m)^__build_info:\n(?:[ \t].*\n|\n)*", "");
    }
    internal static MixedSchema Locate(Settings settings, string? bundledRoot = null)
    {
        var original = Path.Combine(settings.PrebuiltPath, settings.Schema + ".schema.yaml");
        var identity = new StringBuilder("mixed-v2\n").Append(settings.Schema).Append('\n').Append(NormalizeSchema(original));
        var defaults = Path.Combine(settings.PrebuiltPath, "default.yaml");
        if (File.Exists(defaults)) identity.Append('\n').Append(NormalizeSchema(defaults));
        foreach (var table in Directory.EnumerateFiles(settings.PrebuiltPath, "*.table.bin").Order(StringComparer.Ordinal))
            identity.Append('\n').Append(Path.GetFileName(table)).Append(':').Append(Hash(table));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString()))).ToLowerInvariant();
        var cacheRoot = Path.Combine(settings.UserPath, "gamepad-mixed");
        var cached = ReadCache(cacheRoot, fingerprint, original);
        if (cached != null) return cached;
        // Adopt the previous timestamp-based cache without rebuilding it.
        if (!File.Exists(Path.Combine(cacheRoot, fingerprint + ".json")))
        {
            var legacy = Legacy(settings, fingerprint);
            if (legacy.Ready) { legacy.SaveManifest(); return legacy; }
        }
        var bundled = bundledRoot == null ? null : ReadCache(bundledRoot, fingerprint, original);
        if (bundled != null) return bundled.CopyTo(cacheRoot);
        return At(cacheRoot, "gamepad_t9_mixed_" + fingerprint[..16], original, fingerprint);
    }
    private static MixedSchema At(string root, string id, string original, string fingerprint)
    {
        var folder = Path.Combine(root, id["gamepad_t9_mixed_".Length..]);
        return new(id, Path.Combine(folder, "source"), Path.Combine(folder, "build"), original, fingerprint);
    }
    private static MixedSchema? ReadCache(string root, string fingerprint, string original)
    {
        try
        {
            var path = Path.Combine(root, fingerprint + ".json");
            if (!File.Exists(path)) return null;
            var manifest = JsonSerializer.Deserialize<CacheManifest>(File.ReadAllText(path));
            if (manifest == null || !Regex.IsMatch(manifest.Id, @"\Agamepad_t9_mixed_[a-f0-9]{16}\z")) return null;
            var cached = At(root, manifest.Id, original, fingerprint);
            return cached.Ready && Hash(cached.CompiledSchema) == manifest.SchemaHash && Hash(cached.Prism) == manifest.PrismHash ? cached : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    private static MixedSchema Legacy(Settings settings, string fingerprint)
    {
        var original = Path.Combine(settings.PrebuiltPath, settings.Schema + ".schema.yaml");
        var dll = new FileInfo(Path.Combine(settings.InstallRoot, "rime.dll"));
        var dependencies = Directory.EnumerateFiles(settings.PrebuiltPath).Where(p => p.EndsWith(".yaml") || p.EndsWith(".bin"))
            .Order(StringComparer.Ordinal).Select(p => new FileInfo(p)).Select(f => $"{f.Name}:{f.Length}:{f.LastWriteTimeUtc.Ticks}");
        var stamp = "mixed-v2\n" + File.ReadAllText(original) + "\n" + string.Join("\n", dependencies) + $"\n{dll.Length}:{dll.LastWriteTimeUtc.Ticks}";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16].ToLowerInvariant();
        var folder = Path.Combine(settings.UserPath, "gamepad-mixed", key);
        var id = "gamepad_t9_mixed_" + key;
        return new(id, Path.Combine(folder, "source"), Path.Combine(folder, "build"), original, fingerprint);
    }
    internal string CompiledSchema => Path.Combine(Build, Id + ".schema.yaml");
    internal string Prism => Path.Combine(Build, Id + ".prism.bin");
    internal bool Ready => File.Exists(CompiledSchema) && new FileInfo(CompiledSchema).Length > 0 &&
        File.Exists(Prism) && new FileInfo(Prism).Length > 0 && File.Exists(Path.Combine(Build, "ready"));
    internal void SaveManifest()
    {
        if (!Ready) throw new InvalidOperationException("混合索引尚未生成完整。");
        var root = Path.GetDirectoryName(Path.GetDirectoryName(Build))!;
        var path = Path.Combine(root, Fingerprint + ".json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new CacheManifest(Id, Hash(CompiledSchema), Hash(Prism))));
        File.Move(temporary, path, true);
    }
    internal MixedSchema CopyTo(string cacheRoot)
    {
        if (!Ready) throw new InvalidOperationException("没有可复用的混合索引，请先启动 GamePad T9 生成缓存。");
        var target = At(cacheRoot, Id, SchemaFile, Fingerprint);
        Directory.CreateDirectory(target.Build);
        foreach (var file in new[] { CompiledSchema, Prism })
        {
            var destination = Path.Combine(target.Build, Path.GetFileName(file));
            File.Copy(file, destination + ".tmp", true); File.Move(destination + ".tmp", destination, true);
        }
        File.WriteAllText(Path.Combine(target.Build, "ready"), Id);
        target.SaveManifest(); return target;
    }

    internal string WriteSource(string originalSchema)
    {
        var config = new Config();
        if (RimeSchemaOpen(originalSchema, ref config) == 0) throw new InvalidDataException("无法读取原小白拼写方案。");
        List<string> original = [];
        try
        {
            var count = (int)RimeConfigListSize(ref config, "speller/algebra");
            for (var i = 0; i < count; i++) original.Add(Marshal.PtrToStringUTF8(RimeConfigGetCString(ref config, $"speller/algebra/@{i}")) ?? "");
        }
        finally { RimeConfigClose(ref config); }
        var conversion = original.FindIndex(s => Regex.IsMatch(s, @"^xform/\^?[a-z]/[1-9]/$"));
        if (conversion < 0) throw new InvalidDataException("此小白方案的字母组转换格式尚不支持混合输入。");
        var algebra = original.Take(conversion).ToList();
        // Keep a numeric version of every original spelling, including long
        // dictionary codes, without enumerating every combination of a phrase.
        algebra.Add(@"derive/^(.+)$/\U$1/");
        algebra.Add("xlit/ABCDEFGHIJKLMNOPQRSTUVWXYZ/88899944455566611112223333/");
        algebra.Add("xform/;/7/");
        // A Mandarin syllable has at most six letters. Independent substitutions
        // at each position preserve exact letters even when a syllable repeats one.
        string[] groups = ["pqrs", "tuv", "wxyz", "ghi", "jkl", "mno", "", "abc", "def"];
        for (var position = 0; position < 6; position++)
        for (var group = 0; group < groups.Length; group++)
            if (groups[group].Length > 0)
                algebra.Add($"derive/^([a-z0-9]{{{position}}})[{groups[group]}]([a-z0-9]{{0,{5 - position}}})$/${{1}}{group + 1}${{2}}/");
        Directory.CreateDirectory(Source); Directory.CreateDirectory(Build);
        foreach (var compiled in Directory.EnumerateFiles(Path.GetDirectoryName(SchemaFile)!, "*.yaml"))
            File.Copy(compiled, Path.Combine(Source, Path.GetFileName(compiled)), true);
        File.Copy(SchemaFile, Path.Combine(Source, "gamepad_t9_base.yaml"), true);
        var document = new Dictionary<string, object>
        {
            ["schema"] = new { schema_id = Id, name = "GamePad T9 混合拼音", version = "1" },
            ["__include"] = "gamepad_t9_base:/",
            ["__patch"] = new Dictionary<string, object>
            {
                ["speller/alphabet"] = "abcdefghijklmnopqrstuvwxyz0123456789",
                ["speller/algebra"] = algebra,
                ["translator/prism"] = Id
            }
        };
        var path = Path.Combine(Source, Id + ".schema.yaml");
        File.WriteAllText(path, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Config { public nint Pointer; }
    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int RimeSchemaOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string schema, ref Config config);
    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int RimeConfigClose(ref Config config);
    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] private static extern nuint RimeConfigListSize(ref Config config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DllImport("rime.dll", CallingConvention = CallingConvention.Cdecl)] private static extern nint RimeConfigGetCString(ref Config config, [MarshalAs(UnmanagedType.LPUTF8Str)] string key);
}
