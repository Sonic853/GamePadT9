using System.Text.Json;

namespace GamePadT9;

internal static class CacheValidation
{
    internal static int Run(string root)
    {
        var checks = new List<string>();
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("FAILED: " + name); checks.Add(name); }
        var settings = Settings.Load(root);
        var source = MixedSchema.Locate(settings);
        Check(source.Ready, "The existing v2 cache is reused without deployment");
        var originalWrite = File.GetLastWriteTimeUtc(source.Prism);
        // Native Rime file APIs still impose path limits; keep nested fixtures short.
        var fixture = Path.Combine(root, "artifacts", "cv-" + Guid.NewGuid().ToString("N")[..8]);
        var bundle = Path.Combine(fixture, "bundle");
        source.CopyTo(bundle);
        Check(Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories).Count() == 4,
            "The exported cache includes only the manifest, schema, prism and completion marker");
        var isolated = settings with { UserPath = Path.Combine(fixture, "user") };
        Check(!MixedSchema.Locate(isolated).Ready, "A missing cache requires generation");
        var cached = MixedSchema.Locate(isolated, bundle);
        Check(cached.Ready && cached.Id == source.Id && !Directory.Exists(cached.Source), "A fresh user directory imports the bundled cache without compilation");
        var cachedWrite = File.GetLastWriteTimeUtc(cached.Prism);
        Check(MixedSchema.Locate(isolated).Ready && File.GetLastWriteTimeUtc(cached.Prism) == cachedWrite, "Local cache is used directly without rewriting the prism");
        var prebuilt = Path.Combine(fixture, "prebuilt"); Directory.CreateDirectory(prebuilt);
        foreach (var file in Directory.EnumerateFiles(settings.PrebuiltPath))
        {
            File.Copy(file, Path.Combine(prebuilt, Path.GetFileName(file)));
            File.SetLastWriteTimeUtc(Path.Combine(prebuilt, Path.GetFileName(file)), DateTime.UtcNow);
        }
        var moved = isolated with { PrebuiltPath = prebuilt };
        Check(MixedSchema.Locate(moved).Ready && MixedSchema.Locate(moved).Fingerprint == cached.Fingerprint,
            "Moving the deployed files and changing timestamps does not invalidate the cache");
        var schema = Path.Combine(prebuilt, settings.Schema + ".schema.yaml");
        var contents = File.ReadAllText(schema);
        File.WriteAllText(schema, contents.Replace("__build_info:", "__build_info:\n  cache_validation_timestamp: 123456789"));
        Check(MixedSchema.Locate(moved).Ready, "Deployment build metadata does not invalidate the cache");
        File.WriteAllText(schema, contents.Replace("name:", "cache_validation_changed_name:", StringComparison.Ordinal));
        Check(!MixedSchema.Locate(moved, bundle).Ready, "A changed schema cannot use a mismatched bundle");
        File.WriteAllText(schema, contents);
        var table = Directory.EnumerateFiles(prebuilt, "*.table.bin").First();
        using (var file = new FileStream(table, FileMode.Open, FileAccess.ReadWrite))
        { file.Position = file.Length - 1; var value = file.ReadByte(); file.Position--; file.WriteByte((byte)(value ^ 1)); }
        Check(!MixedSchema.Locate(moved, bundle).Ready, "A changed dictionary cannot reuse incompatible syllable IDs");
        var corrupt = Path.Combine(fixture, "corrupt"); var bad = source.CopyTo(corrupt);
        using (var file = new FileStream(bad.Prism, FileMode.Open, FileAccess.Write)) { file.Position = file.Length - 1; file.WriteByte(0xFF); }
        var missing = isolated with { UserPath = Path.Combine(fixture, "missing") };
        Check(!MixedSchema.Locate(missing, corrupt).Ready, "A corrupt bundle is rejected before loading native code");
        // Remove only this fixture's imported prism to exercise the real regeneration path.
        File.Delete(cached.Prism);
        Check(!MixedSchema.Locate(isolated).Ready, "Missing prism falls back to generation even when the manifest remains");
        var script = Path.Combine(settings.UserPath, "rime.lua");
        if (File.Exists(script)) File.Copy(script, Path.Combine(isolated.UserPath, "rime.lua"));
        var lua = Path.Combine(settings.UserPath, "lua");
        if (Directory.Exists(lua)) foreach (var file in Directory.EnumerateFiles(lua, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(isolated.UserPath, "lua", Path.GetRelativePath(lua, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
        }
        using (var engine = new RimeEngine(isolated, useBundledCache: false))
        {
            Check(MixedSchema.Locate(isolated).Ready, "The engine regenerates a missing index and saves a reusable manifest");
            engine.InputLetter('n'); engine.InputRegion(3); engine.InputLetter('h'); engine.InputRegion(1); engine.InputLetter('o');
            Check(Validation.Find(engine, "你好"), "The regenerated index returns the correct mixed candidate");
            engine.Clear();
        }
        Check(File.GetLastWriteTimeUtc(source.Prism) == originalWrite, "Verification never rebuilds or changes the main program's cache");
        var report = new { passed = true, checks, fixture };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artifacts", "cache-validation.json"), json);
        Console.WriteLine(json); return 0;
    }
}
