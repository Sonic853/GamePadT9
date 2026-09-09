using System.Text.Json;
using System.Text.Json.Serialization;

namespace GamePadT9;

[JsonConverter(typeof(JsonStringEnumConverter<ControlSide>))]
internal enum ControlSide { Left, Right }

internal sealed record UserSettings
{
    public string? ControllerId { get; init; }
    public string? ControllerName { get; init; }
    public GamepadFamily ControllerFamily { get; init; } = GamepadFamily.Xbox;
    public ControlSide Stick { get; init; } = ControlSide.Right;
    public ControlSide Trigger { get; init; } = ControlSide.Right;
    public int PanelOpacity { get; init; } = 50;
    public int GridOpacity { get; init; } = 80;
    public int HighlightOpacity { get; init; } = 90;
    // Shared by both input surfaces; retain the key for existing settings files.
    public int PanelBlur { get; init; }
    [JsonIgnore] internal string StickLabel => Stick == ControlSide.Left ? "左摇杆" : "右摇杆";
    [JsonIgnore] internal string StickClickLabel => Stick == ControlSide.Left ? "L3" : "R3";
    [JsonIgnore] internal string DetailStickLabel => Stick == ControlSide.Left ? "RS" : "LS";
    [JsonIgnore] internal string TriggerLabel => Trigger == ControlSide.Left ? "LT" : "RT";
    internal static int Alpha(int percentage) => (percentage * 255 + 50) / 100;
    internal void Validate()
    {
        if (!Enum.IsDefined(Stick) || !Enum.IsDefined(Trigger) || !Enum.IsDefined(ControllerFamily) ||
            (ControllerId != null && string.IsNullOrWhiteSpace(ControllerId)) ||
            PanelOpacity is < 0 or > 100 || GridOpacity is < 0 or > 100 || HighlightOpacity is < 0 or > 100 ||
            PanelBlur is < 0 or > 100)
            throw new InvalidDataException("摇杆、扳机或外观设置无效。可见度和背景模糊程度范围为 0–100%。");
    }
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    internal static UserSettings Load(string root, out string? warning)
    {
        warning = null;
        var path = Path.Combine(root, "user-settings.json");
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("设置文件为空。");
            settings.Validate(); return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { warning = "无法读取界面及手柄设置，已使用默认值：" + ex.Message; return new(); }
    }
    internal void Save(string root)
    {
        Validate();
        var path = Path.Combine(root, "user-settings.json");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
