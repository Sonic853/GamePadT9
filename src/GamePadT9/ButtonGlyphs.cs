using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Xml;
using Svg;

namespace GamePadT9;

// Tokens are used only in authored prompts. Candidate text and user content are always plain text.
internal sealed class ButtonGlyphs : IDisposable
{
    private readonly Dictionary<string, SvgDocument> documents = [];
    private static readonly Regex Tokens = new(@"\[(A|B|X|Y|LB|RB|LT|RT|L3|R3|LS|RS|Left|Right|View|Menu)\]", RegexOptions.Compiled);
    internal static readonly string[] Buttons = ["A", "B", "X", "Y", "LB", "RB", "LT", "RT", "L3", "R3", "LS", "RS", "Left", "Right", "View", "Menu"];
    internal static string FileName(GamepadFamily family, string button)
    {
        var ps = family != GamepadFamily.Xbox;
        var prefix = family == GamepadFamily.PS5 ? "ps5" : "ps4";
        var name = button switch
        {
            "A" => ps ? "ps_color_button_x" : "shared_color_button_a",
            "B" => ps ? "ps_color_button_circle" : "shared_color_button_b",
            "X" => ps ? "ps_color_button_square" : "shared_color_button_x",
            "Y" => ps ? "ps_color_button_triangle" : "shared_color_button_y",
            "LB" => ps ? prefix + "_l1" : "xbox_lb", "RB" => ps ? prefix + "_r1" : "xbox_rb",
            "LT" => ps ? prefix + "_l2" : "xbox_lt", "RT" => ps ? prefix + "_r2" : "xbox_rt",
            "View" => ps ? prefix + (family == GamepadFamily.PS5 ? "_button_create" : "_button_share") : "xbox_button_select",
            "Menu" => ps ? prefix + "_button_options" : "xbox_button_start",
            "L3" => "shared_lstick_click", "R3" => "shared_rstick_click",
            "LS" => "shared_lstick", "RS" => "shared_rstick",
            "Left" => ps ? "ps_dpad_left" : "shared_dpad_left", "Right" => ps ? "ps_dpad_right" : "shared_dpad_right",
            _ => throw new ArgumentOutOfRangeException(nameof(button))
        };
        return name + ".svg";
    }
    internal SvgDocument Get(GamepadFamily family, string button)
    {
        var name = FileName(family, button);
        if (documents.TryGetValue(name, out var document)) return document;
        using var stream = typeof(ButtonGlyphs).Assembly.GetManifestResourceStream("GamePadT9.Assets.Steam." + name)
            ?? throw new InvalidDataException("缺少 Steam 按键图标：" + name);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        document = SvgDocument.Open<SvgDocument>(reader) ?? throw new InvalidDataException("无法读取 Steam SVG 图标：" + name);
        var size = document.GetDimensions();
        if (!(size.Width > 0 && size.Height > 0 && float.IsFinite(size.Width) && float.IsFinite(size.Height)))
            throw new InvalidDataException("Steam SVG 图标尺寸无效：" + name);
        documents.Add(name, document); return document;
    }
    internal void DrawIcon(Graphics graphics, GamepadFamily family, string button, RectangleF bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var document = Get(family, button);
        var size = document.GetDimensions();
        var zoom = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
        var state = graphics.Save();
        try
        {
            graphics.SetClip(bounds, CombineMode.Intersect);
            graphics.TranslateTransform(bounds.X + (bounds.Width - size.Width * zoom) / 2, bounds.Y + (bounds.Height - size.Height * zoom) / 2);
            graphics.ScaleTransform(zoom, zoom);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            // Render paths at the caller's final DPI / scale, without a fixed-resolution icon bitmap.
            document.Draw(graphics);
        }
        finally { graphics.Restore(state); }
    }
    internal void Draw(Graphics graphics, string prompt, GamepadFamily family, Font font, Color color, RectangleF bounds, float iconSize = 23, bool center = false)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap };
        using var brush = new SolidBrush(color);
        var parts = new List<(string Text, bool Icon, float Width)>();
        void Text(string text) { if (text.Length > 0) parts.Add((text, false, graphics.MeasureString(text, font, int.MaxValue, format).Width)); }
        var start = 0;
        foreach (Match match in Tokens.Matches(prompt))
        { Text(prompt[start..match.Index]); parts.Add((match.Groups[1].Value, true, iconSize + 2)); start = match.Index + match.Length; }
        Text(prompt[start..]);
        var width = parts.Sum(x => x.Width);
        var fit = Math.Min(1, bounds.Width / Math.Max(1, width));
        var state = graphics.Save();
        try
        {
            graphics.SetClip(bounds, CombineMode.Intersect);
            graphics.TranslateTransform(bounds.X + (center ? (bounds.Width - width * fit) / 2 : 0), bounds.Y + bounds.Height / 2);
            graphics.ScaleTransform(fit, fit);
            float x = 0;
            foreach (var part in parts)
            {
                if (part.Icon) DrawIcon(graphics, family, part.Text, new(x, -iconSize / 2, iconSize, iconSize));
                else graphics.DrawString(part.Text, font, brush, x, -font.GetHeight(graphics) / 2, format);
                x += part.Width;
            }
        }
        finally { graphics.Restore(state); }
    }
    public void Dispose() => documents.Clear();
}
