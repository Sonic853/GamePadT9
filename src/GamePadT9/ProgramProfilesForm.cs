using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GamePadT9;

internal sealed class ProgramProfilesForm : Form
{
    private sealed record Entry(string? Path) { public override string ToString() => Path == null ? "全局配置" : System.IO.Path.GetFileName(Path); }
    private readonly ListBox list = new() { Name = "ProgramList", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Label pathLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true };
    private readonly ComboBox mode = Selector("InputModeSelector");
    private readonly ComboBox destination = Selector("CompletionSelector");
    private readonly GroupBox completion = new() { Text = "外部输入框完成操作", Dock = DockStyle.Fill, ForeColor = Color.WhiteSmoke };
    private readonly Dictionary<string, InputBehavior> programs = new(StringComparer.OrdinalIgnoreCase);
    private InputBehavior global;
    private Entry? current;
    private bool loading;
    internal ProgramProfiles Draft { get { Store(); return new() { Global = global, Programs = programs.Select(p => new ProgramProfile(p.Key, p.Value)).ToList() }; } }
    internal ProgramProfilesForm(ProgramProfiles settings, Action<ProgramProfiles> save)
    {
        global = settings.Global; foreach (var p in settings.Programs) programs.Add(p.Path, p.Behavior);
        Text = "GamePad T9 · 程序列表"; Name = "ProgramProfilesWindow";
        ClientSize = new(850, 570); MinimumSize = new(700, 550); StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new(96, 96);
        Font = new("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(20, 24, 29); ForeColor = Color.WhiteSmoke;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 245)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 50)); Controls.Add(layout);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        left.RowStyles.Add(new(SizeType.Percent, 100)); for (var i = 0; i < 3; i++) left.RowStyles.Add(new(SizeType.Absolute, 42));
        left.Controls.Add(list, 0, 0);
        var browse = MakeButton("添加 EXE…", "AddExecutable"); var running = MakeButton("从运行中的程序添加…", "AddRunningProgram"); var remove = MakeButton("移除独立配置，继承全局", "RemoveProgram");
        left.Controls.Add(browse, 0, 1); left.Controls.Add(running, 0, 2); left.Controls.Add(remove, 0, 3); layout.Controls.Add(left, 0, 0);
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18, 0, 0, 0), ColumnCount = 1, RowCount = 4 };
        foreach (var height in new[] { 70, 85, 85, 120 }) right.RowStyles.Add(new(SizeType.Absolute, height));
        right.Controls.Add(pathLabel, 0, 0);
        mode.Items.AddRange(["无屏蔽操作", "游戏失去焦点", "使用外部输入框"]);
        destination.Items.AddRange(["完成输入后复制到剪切板", "完成输入后填入至目标程序"]);
        var modes = new GroupBox { Text = "输入方式", Dock = DockStyle.Fill, ForeColor = ForeColor, Padding = new(12, 10, 12, 10) };
        modes.Controls.Add(mode); right.Controls.Add(modes, 0, 1);
        completion.Padding = new(12, 10, 12, 10);
        completion.Controls.Add(destination); right.Controls.Add(completion, 0, 2);
        right.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "独立配置优先；未添加的程序使用全局配置。\n外部输入框：短按 A 选词，长按 A 完成。\n失焦模式依赖游戏停止后台手柄输入。\n配置在下一次开启输入时生效。", Padding = new(0, 12, 0, 0) }, 0, 3);
        layout.Controls.Add(right, 1, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = MakeButton("取消", "CancelProfiles"); var apply = MakeButton("保存", "SaveProfiles"); cancel.Dock = apply.Dock = DockStyle.None;
        cancel.Size = apply.Size = new(100, 38); buttons.Controls.AddRange([apply, cancel]); layout.Controls.Add(buttons, 0, 1); layout.SetColumnSpan(buttons, 2);
        cancel.Click += (_, _) => Close(); apply.Click += (_, _) => { try { save(Draft); DialogResult = DialogResult.OK; Close(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "保存失败"); } };
        CancelButton = cancel;
        list.SelectedIndexChanged += (_, _) => { Store(); current = list.SelectedItem as Entry; LoadCurrent(); remove.Enabled = current?.Path != null; };
        mode.SelectedIndexChanged += (_, _) => completion.Enabled = mode.SelectedIndex == (int)InputFocusMode.External;
        browse.Click += (_, _) => { using var dialog = new OpenFileDialog { Filter = "程序 (*.exe)|*.exe", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) AddProgram(dialog.FileName); };
        running.Click += (_, _) => PickRunning();
        remove.Click += (_, _) => { if (current?.Path is not string path) return; programs.Remove(path); current = null; list.Items.Remove(list.SelectedItem!); list.SelectedIndex = 0; };
        list.Items.Add(new Entry(null)); foreach (var p in programs.Keys) list.Items.Add(new Entry(p)); list.SelectedIndex = 0;
    }
    internal void AddProgram(string path)
    {
        path = System.IO.Path.GetFullPath(path); Store();
        if (!programs.ContainsKey(path)) { programs.Add(path, global); list.Items.Add(new Entry(path)); }
        list.SelectedItem = list.Items.Cast<Entry>().First(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
    }
    private void Store()
    {
        if (loading || current == null) return;
        var behavior = new InputBehavior { Mode = (InputFocusMode)mode.SelectedIndex, Completion = (CompletionDestination)destination.SelectedIndex };
        if (current.Path == null) global = behavior; else programs[current.Path] = behavior;
    }
    private void LoadCurrent()
    {
        if (current == null) return; loading = true;
        try
        {
            var value = current.Path == null ? global : programs[current.Path];
            pathLabel.Text = current.Path ?? "全局配置\n未设置独立配置的程序均使用此配置。";
            mode.SelectedIndex = (int)value.Mode;
            destination.SelectedIndex = (int)value.Completion; completion.Enabled = value.Mode == InputFocusMode.External;
        }
        finally { loading = false; }
    }
    private void PickRunning()
    {
        using var picker = new Form { Text = "选择运行中的程序", ClientSize = new(700, 420), StartPosition = FormStartPosition.CenterParent, Font = Font };
        var choices = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        var paths = new List<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses()) using (process)
        {
            try
            {
                if (process.Id == Environment.ProcessId || process.MainWindowHandle == 0 || process.MainModule?.FileName is not string path || !seen.Add(path)) continue;
                paths.Add(path); choices.Items.Add($"{process.MainWindowTitle} — {path}");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
        }
        var add = new Button { Text = "添加所选程序", Dock = DockStyle.Bottom, Height = 40 };
        void Select() { if (choices.SelectedIndex < 0) return; AddProgram(paths[choices.SelectedIndex]); picker.Close(); }
        add.Click += (_, _) => Select(); choices.DoubleClick += (_, _) => Select(); picker.Controls.Add(choices); picker.Controls.Add(add); picker.ShowDialog(this);
    }
    protected override void OnShown(EventArgs e) { base.OnShown(e); ShowWindow(Handle, 1); }
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    private static ComboBox Selector(string name) => new() { Name = name, Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(33, 39, 46), ForeColor = Color.WhiteSmoke };
    private static Button MakeButton(string text, string name) => new() { Text = text, Name = name, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33, 39, 46), ForeColor = Color.WhiteSmoke };
}
