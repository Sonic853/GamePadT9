using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace GamePadT9;

// Regions replace the panel alpha instead of stacking translucent backgrounds.
public sealed class ExternalInputBackground : FrameworkElement
{
    internal UserSettings Settings { get; set; } = new();
    internal (FrameworkElement Element, bool Highlight)[] Regions { get; set; } = [];
    protected override void OnRender(DrawingContext context)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var panel = new GeometryGroup { FillRule = FillRule.EvenOdd };
        panel.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 6, 6));
        var regions = new List<(Geometry Shape, bool Highlight)>();
        foreach (var (element, highlight) in Regions)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) continue;
            var origin = element.TranslatePoint(new Point(), this);
            var shape = new RectangleGeometry(new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight), 4, 4);
            panel.Children.Add(shape); regions.Add((shape, highlight));
        }
        context.DrawGeometry(Fill(20, 24, 29, Settings.PanelOpacity), null, panel);
        foreach (var (shape, highlight) in regions)
            context.DrawGeometry(highlight ? Fill(110, 236, 169, Settings.HighlightOpacity) : Fill(33, 39, 46, Settings.GridOpacity), null, shape);
    }
    private static System.Windows.Media.Brush Fill(byte r, byte g, byte b, int opacity) => new SolidColorBrush(Color.FromArgb((byte)UserSettings.Alpha(opacity), r, g, b));
}
