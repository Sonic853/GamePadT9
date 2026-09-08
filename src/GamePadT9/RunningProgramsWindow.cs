using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using ListBox = System.Windows.Controls.ListBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Orientation = System.Windows.Controls.Orientation;

namespace GamePadT9;

internal sealed class RunningProgramsWindow : PanelWindow
{
    private sealed record Entry(string Title, string Path) { public override string ToString() => Title + "\n" + Path; }
    private readonly ListBox choices = new() { Margin = new(24, 0, 24, 16), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch };
    private readonly Wpf.Ui.Controls.TextBox search = new() { PlaceholderText = "搜索窗口名称或程序路径", Margin = new(24, 12, 24, 12) };
    private readonly TextBlock status = new() { Margin = new(24, 0, 24, 12), TextWrapping = TextWrapping.Wrap };
    private readonly Wpf.Ui.Controls.Button add = new() { Content = "添加所选程序", IsDefault = true };
    private List<Entry> entries = [];
    internal string? SelectedPath { get; private set; }
    internal RunningProgramsWindow()
    {
        Title = "选择运行中的程序"; Width = 760; Height = 540; MinWidth = 560; MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var layout = new DockPanel(); Content = layout;
        var title = new TitleBar { Title = Title, ShowMinimize = false, ShowMaximize = false };
        DockPanel.SetDock(title, Dock.Top); layout.Children.Add(title);
        DockPanel.SetDock(search, Dock.Top); layout.Children.Add(search);
        DockPanel.SetDock(status, Dock.Top); layout.Children.Add(status);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new(24, 0, 24, 20) };
        var refresh = new Wpf.Ui.Controls.Button { Content = "刷新", Margin = new(0, 0, 10, 0) };
        var cancel = new Wpf.Ui.Controls.Button { Content = "取消", IsCancel = true, Margin = new(0, 0, 10, 0) };
        add.Style = (Style)FindResource("PrimaryAction");
        footer.Children.Add(refresh); footer.Children.Add(cancel); footer.Children.Add(add);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer); layout.Children.Add(choices);
        choices.ItemContainerStyle = (Style)FindResource("SideItem");
        choices.ItemTemplate = (DataTemplate)FindResource("RunningProgramTemplate");
        choices.Background = System.Windows.Media.Brushes.Transparent; choices.BorderThickness = new(0);
        choices.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        choices.SelectionChanged += (_, _) => add.IsEnabled = choices.SelectedItem != null;
        choices.MouseDoubleClick += (_, _) => Select(); add.Click += (_, _) => Select();
        cancel.Click += (_, _) => Close(); refresh.Click += (_, _) => Refresh();
        search.TextChanged += (_, _) => Filter();
        Refresh();
    }
    private void Refresh()
    {
        entries = []; var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == 0 || !IsWindowVisible(process.MainWindowHandle) || string.IsNullOrWhiteSpace(process.MainWindowTitle)) continue;
                var path = ProgramProfiles.Executable((uint)process.Id);
                if (path != null && seen.Add(path)) entries.Add(new(process.MainWindowTitle, path));
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        Filter();
    }
    private void Filter()
    {
        var query = search.Text.Trim();
        var filtered = entries.Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.Title).ToArray();
        choices.ItemsSource = filtered; choices.SelectedIndex = filtered.Length > 0 ? 0 : -1;
        status.Text = filtered.Length == 0 ? "没有找到可添加的窗口，也可以返回后直接选择 EXE 文件。" : $"找到 {filtered.Length} 个程序，选择后添加独立配置。";
        add.IsEnabled = choices.SelectedItem != null;
    }
    private void Select() { if (choices.SelectedItem is not Entry entry) return; SelectedPath = entry.Path; DialogResult = true; }
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
}
