using System.Windows;
using System.Windows.Controls;

namespace GamePadT9;

internal sealed partial class ProgramProfilesForm : PanelWindow
{
    private sealed record Entry(string? Path)
    {
        public string Name => Path == null ? "全局配置" : System.IO.Path.GetFileName(Path);
        public string Detail => Path ?? "所有未单独配置的程序";
        public string Kind => Path == null ? "默认" : "独立";
    }
    private readonly Dictionary<string, InputBehavior> programs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<ProgramProfiles> save;
    private InputBehavior global;
    private Entry? current;
    private bool loading;
    internal ProgramProfiles Draft { get { Store(); return new() { Global = global, Programs = programs.Select(p => new ProgramProfile(p.Key, p.Value)).ToList() }; } }
    internal ProgramProfilesForm(ProgramProfiles settings, Action<ProgramProfiles> save, GamepadFamily family = GamepadFamily.Xbox)
    {
        this.save = save; global = settings.Global;
        foreach (var p in settings.Programs) programs.Add(p.Path, p.Behavior);
        InitializeComponent();
        CompletionHint.Children.Insert(0, new SteamGlyph { Token = "A", Family = family, Margin = new(0, 0, 12, 0) });
        InputModeSelector.ItemsSource = new[] { "无屏蔽操作", "游戏失去焦点", "使用外部输入框" };
        CompletionSelector.ItemsSource = new[] { "完成输入后复制到剪切板", "完成输入后填入至目标程序" };
        RefreshList(null);
    }
    internal void AddProgram(string path)
    {
        path = System.IO.Path.GetFullPath(path); Store();
        if (!programs.ContainsKey(path)) programs.Add(path, global);
        loading = true; try { ProgramSearch.Text = ""; } finally { loading = false; }
        RefreshList(path);
    }
    private void Store()
    {
        if (loading || current == null || InputModeSelector.SelectedIndex < 0 || CompletionSelector.SelectedIndex < 0) return;
        var behavior = new InputBehavior { Mode = (InputFocusMode)InputModeSelector.SelectedIndex, Completion = (CompletionDestination)CompletionSelector.SelectedIndex };
        if (current.Path == null) global = behavior; else programs[current.Path] = behavior;
    }
    private void LoadCurrent()
    {
        if (current == null) return;
        loading = true;
        try
        {
            var value = current.Path == null ? global : programs[current.Path];
            ProfileHeading.Text = current.Name;
            ProfilePath.Text = current.Path ?? "未设置独立配置的程序使用以下选项。";
            ProfileBadge.Text = current.Path == null ? "全局默认" : "独立配置";
            InputModeSelector.SelectedIndex = (int)value.Mode; CompletionSelector.SelectedIndex = (int)value.Completion;
            RemoveProgram.IsEnabled = current.Path != null;
            UpdateMode();
        }
        finally { loading = false; }
    }
    private void RefreshList(string? selectedPath)
    {
        loading = true;
        try
        {
            var query = ProgramSearch.Text.Trim();
            var entries = new List<Entry> { new(null) };
            entries.AddRange(programs.Keys.Where(path => query.Length == 0 || path.Contains(query, StringComparison.OrdinalIgnoreCase)).OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase).Select(path => new Entry(path)));
            ProgramList.ItemsSource = entries;
            current = entries.FirstOrDefault(e => string.Equals(e.Path, selectedPath, StringComparison.OrdinalIgnoreCase)) ?? entries[0];
            ProgramList.SelectedItem = current;
            ProgramCount.Text = programs.Count == 0 ? "暂未添加独立配置" : $"{programs.Count} 个独立配置";
            SearchStatus.Text = query.Length > 0 && entries.Count == 1 ? "没有匹配的程序" : "";
        }
        finally { loading = false; }
        LoadCurrent();
    }
    private void SelectedProgram(object sender, SelectionChangedEventArgs e)
    {
        if (loading || !ReferenceEquals(e.OriginalSource, ProgramList)) return;
        Store(); current = ProgramList.SelectedItem as Entry; LoadCurrent();
    }
    private void Search(object sender, TextChangedEventArgs e) { if (!loading && ProgramList != null) { Store(); RefreshList(current?.Path); } }
    private void ChangeMode(object sender, SelectionChangedEventArgs e) { if (CompletionCard != null) UpdateMode(); }
    private void UpdateMode()
    {
        var external = InputModeSelector.SelectedIndex == (int)InputFocusMode.External;
        CompletionCard.IsEnabled = external;
        CompletionSelector.IsEnabled = external;
        CompletionCard.Opacity = external ? 1 : .55;
        ModeDescription.Text = InputModeSelector.SelectedIndex switch
        {
            (int)InputFocusMode.Defocus => "开启输入时让游戏失去焦点。选词或输入数字后，短暂返回游戏填入文字，再回到输入面板。效果取决于游戏是否停止后台手柄响应。",
            (int)InputFocusMode.External => "在独立输入框中编辑文字，完成后执行下面选择的操作。",
            _ => "目标程序保持焦点，直接接收文字；程序也可能同时接收手柄操作。"
        };
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "程序 (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AddProgram(dialog.FileName);
    }
    private void PickRunning(object sender, RoutedEventArgs e)
    {
        using var picker = new RunningProgramsWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPath is string path) AddProgram(path);
    }
    private void Remove(object sender, RoutedEventArgs e)
    {
        if (current?.Path is not string path) return;
        programs.Remove(path); current = null; RefreshList(null);
    }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private void Save(object sender, RoutedEventArgs e)
    {
        try { var value = Draft; value.Validate(); save(value); Close(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        { SaveError.Message = "无法保存配置：" + ex.Message; SaveError.IsOpen = true; }
    }
}
