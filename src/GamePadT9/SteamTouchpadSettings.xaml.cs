using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wpf.Ui.Controls;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace GamePadT9;

internal partial class SteamTouchpadSettings : System.Windows.Controls.UserControl
{
    private bool updating;
    internal int LeftAngle => Angle(LeftValue);
    internal int RightAngle => Angle(RightValue);
    internal bool Linked => AdjustmentMode.SelectedIndex == 1;
    public SteamTouchpadSettings()
    {
        InitializeComponent();
        LeftSlider.ValueChanged += (_, _) => Change(true, LeftSlider.Value);
        RightSlider.ValueChanged += (_, _) => Change(false, RightSlider.Value);
        LeftValue.ValueChanged += (_, _) => Change(true, LeftValue.Value);
        RightValue.ValueChanged += (_, _) => Change(false, RightValue.Value);
        AdjustmentMode.SelectionChanged += (_, _) => { if (!updating && Linked) Change(true, LeftValue.Value); };
        SetValue(new());
    }
    private static int Angle(NumberBox box) => box.Value is double value && double.IsFinite(value) && value is >= -180 and <= 180
        ? (int)Math.Round(value) : throw new InvalidDataException("请输入 -180° 至 180° 的触控板角度。");
    private void Change(bool left, double? value)
    {
        if (updating || value is not double degrees || !double.IsFinite(degrees)) return;
        degrees = Math.Clamp(Math.Round(degrees), -180, 180);
        updating = true;
        try
        {
            if (left || Linked) LeftValue.Value = LeftSlider.Value = LeftPreview.Angle = left ? degrees : -degrees;
            if (!left || Linked) RightValue.Value = RightSlider.Value = RightPreview.Angle = left ? -degrees : degrees;
        }
        finally { updating = false; }
    }
    internal void SetValue(UserSettings settings)
    {
        updating = true;
        try
        {
            AdjustmentMode.SelectedIndex = settings.Steam2015LinkAngles ? 1 : 0;
            LeftValue.Value = LeftSlider.Value = LeftPreview.Angle = settings.Steam2015LeftAngle;
            RightValue.Value = RightSlider.Value = RightPreview.Angle = settings.Steam2015RightAngle;
        }
        finally { updating = false; }
    }
    private void Reset(object sender, RoutedEventArgs e) { Change(true, 0); Change(false, 0); }
}

// The preview shows the layout on the physical pad, so its rotation is the
// inverse of the coordinate transform used when reading a finger position.
internal sealed class TouchpadLayoutPreview : FrameworkElement
{
    private double angle;
    internal double Angle { get => angle; set { angle = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        var center = new System.Windows.Point(ActualWidth / 2, ActualHeight / 2);
        var radius = Math.Min(ActualWidth, ActualHeight) / 2 - 3;
        var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(98, 224, 170));
        dc.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 26, 30)), new Pen(accent, 1), center, radius, radius);
        dc.PushClip(new EllipseGeometry(center, radius - 3, radius - 3));
        dc.PushTransform(new RotateTransform(Angle, center.X, center.Y));
        var size = radius * 1.45 / 3;
        for (var i = 0; i < 9; i++)
        {
            var box = new Rect(center.X + (i % 3 - 1.5) * size, center.Y + (i / 3 - 1.5) * size, size - 2, size - 2);
            dc.DrawRectangle(i == 1 ? accent : new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 49, 53)), null, box);
            var text = new FormattedText((i + 1).ToString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, i == 1 ? Brushes.Black : Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new System.Windows.Point(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
        }
        dc.Pop(); dc.Pop();
    }
}
