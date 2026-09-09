using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Border = System.Windows.Controls.Border;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBlock = System.Windows.Controls.TextBlock;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace GamePadT9;

public sealed partial class LetterLayoutPicker : System.Windows.Controls.UserControl
{
    private readonly RadioButton[] choices = new RadioButton[4];
    private readonly Border[,] cells = new Border[4, 4];
    private bool updating, dragging;
    private int source = -1, target = -1;
    private Point start, offset;
    private LetterLayout? inheritedLayout;
    private string inheritedCustom = LetterLayouts.DefaultOrder;
    internal LetterLayout SelectedLayout { get; private set; }
    internal string CustomOrder { get; private set; } = LetterLayouts.DefaultOrder;
    internal event Action? Changed;
    private bool Editable => IsEnabled && inheritedLayout == null && SelectedLayout == LetterLayout.Custom;
    internal RadioButton Choice(int index) => choices[index];
    internal Border CustomCell(int index) => cells[3, index];
    public LetterLayoutPicker()
    {
        InitializeComponent();
        var group = Guid.NewGuid().ToString("N");
        string[] names = ["默认", "方案二", "方案三", "自定义"];
        for (var i = 0; i < 4; i++)
        {
            var index = i;
            var grid = new UniformGrid { Rows = 2, Columns = 2 };
            for (var slot = 0; slot < 4; slot++)
            {
                var position = slot;
                var cell = new Border { Height = 50, Margin = new(1), BorderThickness = new(1), BorderBrush = Brushes.Transparent,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 39, 46)),
                    Child = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White } };
                cells[i, slot] = cell; grid.Children.Add(cell);
                if (i == 3) cell.PreviewMouseLeftButtonDown += (_, e) => StartDrag(position, e);
            }
            var choice = new RadioButton { Style = (Style)FindResource("LayoutChoice"), GroupName = group, Content = grid, Tag = i == 3 ? "Custom" : null };
            AutomationProperties.SetName(choice, names[i] + "四格排序");
            choice.Checked += (_, _) =>
            {
                if (updating || !IsEnabled || inheritedLayout != null) return;
                CancelDrag(); SelectedLayout = (LetterLayout)index; Refresh(); Changed?.Invoke();
            };
            choices[i] = choice; Choices.Children.Add(choice);
        }
        PreviewMouseMove += MoveDrag;
        PreviewMouseLeftButtonUp += FinishDrag;
        LostMouseCapture += (_, _) => CancelDrag();
        PreviewKeyDown += (_, e) => { if (source >= 0 && e.Key == Key.Escape) { CancelDrag(); e.Handled = true; } };
        IsEnabledChanged += (_, _) => { CancelDrag(); Refresh(); };
        Unloaded += (_, _) => CancelDrag();
        Refresh();
    }
    internal void SetValue(LetterLayout layout, string custom)
    {
        if (!Enum.IsDefined(layout) || !LetterLayouts.Valid(custom)) throw new InvalidDataException("四格排序无效。");
        CancelDrag(); SelectedLayout = layout; CustomOrder = custom; Refresh();
    }
    internal void Inherit(LetterLayout? layout, string custom = LetterLayouts.DefaultOrder)
    {
        CancelDrag(); inheritedLayout = layout; inheritedCustom = custom; Refresh();
    }
    private void Refresh()
    {
        updating = true;
        try
        {
            for (var i = 0; i < 4; i++)
            {
                choices[i].IsChecked = i == (int)(inheritedLayout ?? SelectedLayout);
                var order = LetterLayouts.Order((LetterLayout)i, inheritedLayout != null ? inheritedCustom : CustomOrder);
                for (var slot = 0; slot < 4; slot++)
                {
                    var label = (TextBlock)cells[i, slot].Child;
                    label.Text = order[slot] == '4' ? "" : ((char)('A' + order[slot] - '1')).ToString();
                    label.FontSize = 24; label.FontWeight = FontWeights.SemiBold;
                    cells[i, slot].ToolTip = order[slot] == '4' ? "空白（四字母组的第 4 个字母）" : label.Text;
                    cells[i, slot].Cursor = i == 3 && Editable ? Cursors.SizeAll : Cursors.Arrow;
                }
            }
        }
        finally { updating = false; }
    }
    private void StartDrag(int index, MouseButtonEventArgs e)
    {
        if (!Editable) return;
        choices[3].Focus(); source = index; target = -1; dragging = false;
        start = e.GetPosition(this); offset = e.GetPosition(cells[3, index]);
        if (!Mouse.Capture(this)) { source = -1; return; }
        e.Handled = true;
    }
    private int Hit(Point point)
    {
        for (var i = 0; i < 4; i++)
        {
            var cell = cells[3, i];
            if (new Rect(cell.TranslatePoint(new Point(), this), cell.RenderSize).Contains(point)) return i;
        }
        return -1;
    }
    private void MoveDrag(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (source < 0) return;
        if (!Editable || e.LeftButton != MouseButtonState.Pressed) { CancelDrag(); return; }
        var point = e.GetPosition(this);
        if (!dragging && Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        dragging = true; target = Hit(point);
        DragLabel.Text = ((TextBlock)cells[3, source].Child).Text;
        DragLabel.FontSize = 24;
        DragTile.Width = cells[3, source].ActualWidth; DragTile.Height = cells[3, source].ActualHeight;
        DragTile.Visibility = Visibility.Visible;
        var canvasPoint = e.GetPosition(DragLayer);
        Canvas.SetLeft(DragTile, canvasPoint.X - offset.X); Canvas.SetTop(DragTile, canvasPoint.Y - offset.Y);
        for (var i = 0; i < 4; i++)
        {
            cells[3, i].Opacity = i == source ? 0.35 : 1;
            cells[3, i].BorderBrush = i == target && i != source ? (System.Windows.Media.Brush)FindResource("PanelAccent") : Brushes.Transparent;
        }
        e.Handled = true;
    }
    private void FinishDrag(object sender, MouseButtonEventArgs e)
    {
        if (source < 0) return;
        var from = source; var to = Hit(e.GetPosition(this));
        var swap = Editable && dragging && to >= 0 && from != to;
        CancelDrag();
        if (swap) { CustomOrder = LetterLayouts.Swap(CustomOrder, from, to); Refresh(); Changed?.Invoke(); }
        e.Handled = true;
    }
    private void CancelDrag()
    {
        source = target = -1; dragging = false;
        DragTile.Visibility = Visibility.Collapsed;
        for (var i = 0; i < 4; i++)
            if (cells[3, i] is { } cell) { cell.Opacity = 1; cell.BorderBrush = Brushes.Transparent; }
        if (ReferenceEquals(Mouse.Captured, this)) Mouse.Capture(null);
    }
}
