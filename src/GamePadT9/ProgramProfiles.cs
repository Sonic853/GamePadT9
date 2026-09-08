using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamePadT9;

[JsonConverter(typeof(JsonStringEnumConverter<InputFocusMode>))]
internal enum InputFocusMode { None, Defocus, External }
[JsonConverter(typeof(JsonStringEnumConverter<CompletionDestination>))]
internal enum CompletionDestination { Clipboard, Target }
internal sealed record InputBehavior
{
    public InputFocusMode Mode { get; init; }
    public CompletionDestination Completion { get; init; } = CompletionDestination.Target;
    internal void Validate()
    {
        if (!Enum.IsDefined(Mode) || !Enum.IsDefined(Completion)) throw new InvalidDataException("程序输入配置无效。");
    }
}
internal sealed record ProgramProfile(string Path, InputBehavior Behavior);
internal sealed record ProgramProfiles
{
    public InputBehavior Global { get; init; } = new();
    public List<ProgramProfile> Programs { get; init; } = [];
    internal InputBehavior Resolve(string? path) => Programs.FirstOrDefault(p => string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase))?.Behavior ?? Global;
    internal static string? Executable(InputWindow window)
    {
        try { using var process = Process.GetProcessById((int)window.Process); return process.MainModule?.FileName; }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }
    internal void Validate()
    {
        if (Global == null || Programs == null) throw new InvalidDataException("程序配置为空。");
        Global.Validate(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Programs)
        {
            if (p == null || p.Behavior == null || string.IsNullOrWhiteSpace(p.Path) || !System.IO.Path.IsPathFullyQualified(p.Path) || !paths.Add(p.Path))
                throw new InvalidDataException("程序路径无效或重复。");
            p.Behavior.Validate();
        }
    }
    internal static ProgramProfiles Load(string root, out string? warning)
    {
        warning = null; var file = System.IO.Path.Combine(root, "program-profiles.json");
        if (!File.Exists(file)) return new();
        try { var value = JsonSerializer.Deserialize<ProgramProfiles>(File.ReadAllText(file)) ?? throw new InvalidDataException("程序配置为空。"); value.Validate(); return value; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { warning = "无法读取程序配置，已使用全局无屏蔽操作：" + ex.Message; return new(); }
    }
    internal void Save(string root)
    {
        Validate(); var path = System.IO.Path.Combine(root, "program-profiles.json"); var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
