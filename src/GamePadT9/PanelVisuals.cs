using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace GamePadT9;

// Reuses the embedded SVG originals, rendered at the WPF element's actual display DPI.
internal sealed class SteamGlyph : FrameworkElement
{
    internal string Token { get; set; } = "A";
    private GamepadFamily family;
    private BitmapSource? image;
    private int pixels;
    internal GamepadFamily Family { get => family; set { if (family != value) { family = value; image = null; InvalidateVisual(); } } }
    internal SteamGlyph() { Width = Height = 30; }
    protected override void OnRender(DrawingContext context)
    {
        var size = Math.Max(1, (int)Math.Ceiling(Math.Max(ActualWidth, ActualHeight) * VisualTreeHelper.GetDpi(this).DpiScaleX));
        if (image == null || pixels != size)
        {
            using var glyphs = new ButtonGlyphs();
            using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bitmap)) { g.Clear(System.Drawing.Color.Transparent); glyphs.DrawIcon(g, family, Token, new(0, 0, size, size)); }
            var data = bitmap.LockBits(new(0, 0, size, size), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            try
            {
                var bytes = new byte[data.Stride * size]; Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
                image = BitmapSource.Create(size, size, 96, 96, PixelFormats.Pbgra32, null, bytes, data.Stride); image.Freeze(); pixels = size;
            }
            finally { bitmap.UnlockBits(data); }
        }
        context.DrawImage(image, new Rect(0, 0, ActualWidth, ActualHeight));
    }
}

public sealed class PanelVisibilityPreview : FrameworkElement
{
    private UserSettings settings = new();
    internal UserSettings Settings { get => settings; set { settings = value; InvalidateVisual(); } }
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(availableSize.Width, 600), 206);
    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth; var height = ActualHeight;
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height), 8, 8));
        var dark = new SolidColorBrush(Color.FromRgb(85, 101, 110));
        var light = new SolidColorBrush(Color.FromRgb(127, 142, 150));
        for (var y = 0; y < height; y += 16) for (var x = 0; x < width; x += 16)
            dc.DrawRectangle((x / 16 + y / 16) % 2 == 0 ? light : dark, null, new Rect(x, y, 16, 16));
        // Draw panel/grid separately so each layer has the same independent visibility as the overlay.
        var gridWidth = Math.Min(190, width * .46); var cellWidth = (gridWidth - 12) / 3;
        var panel = new StreamGeometry();
        using (var path = panel.Open())
        {
            path.BeginFigure(new Point(0, 0), true, true); path.LineTo(new(width, 0), true, false); path.LineTo(new(width, height), true, false); path.LineTo(new(0, height), true, false);
            for (var i = 0; i < 9; i++)
            {
                var r = Cell(i); path.BeginFigure(r.TopLeft, true, true); path.LineTo(r.TopRight, true, false); path.LineTo(r.BottomRight, true, false); path.LineTo(r.BottomLeft, true, false);
            }
        }
        panel.FillRule = FillRule.EvenOdd;
        dc.DrawGeometry(Fill(20, 24, 29, settings.PanelOpacity), null, panel);
        for (var i = 0; i < 9; i++)
        {
            var r = Cell(i); dc.DrawRectangle(i == 4 ? Fill(110, 236, 169, settings.HighlightOpacity) : Fill(33, 39, 46, settings.GridOpacity), null, r);
            Text((i + 1).ToString(), 17, i == 4 ? Brushes.Black : Brushes.White, r.X + r.Width / 2, r.Y + 15, true);
        }
        Text("你好", 25, Brushes.White, gridWidth + 34, 42);
        Text("九宫格实时预览", 13, Brushes.White, gridWidth + 34, 88);
        Text("文字始终保持清晰", 13, Brushes.White, gridWidth + 34, 116);
        dc.Pop();
        Rect Cell(int i) => new(12 + i % 3 * (cellWidth + 6), 16 + i / 3 * 58, cellWidth, 52);
        void Text(string text, double size, Brush brush, double x, double y, bool center = false)
        {
            var value = new FormattedText(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(value, new Point(center ? x - value.Width / 2 : x, y));
        }
    }
    private static Brush Fill(byte r, byte g, byte b, int visibility) => new SolidColorBrush(Color.FromArgb((byte)UserSettings.Alpha(visibility), r, g, b));
}
