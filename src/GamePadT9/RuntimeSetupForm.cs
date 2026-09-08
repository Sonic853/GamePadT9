using System.Text.Json;

namespace GamePadT9;

internal sealed class RuntimeSetupForm : Form
{
    internal Settings? Result { get; private set; }
    internal RuntimeSetupForm(string root, string error)
    {
        Text = "GamePad T9 · 检测小白 T9"; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new(640, 310); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        Font = new("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 3, RowCount = 4 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 130)); layout.ColumnStyles.Add(new(SizeType.Percent, 100)); layout.ColumnStyles.Add(new(SizeType.Absolute, 80));
        foreach (var height in new[] { 120, 52, 52, 50 }) layout.RowStyles.Add(new(SizeType.Absolute, height)); Controls.Add(layout);
        var status = new Label { Dock = DockStyle.Fill, Text = error + "\n请选择本机小白 T9 的安装和用户数据目录。" };
        layout.Controls.Add(status, 0, 0); layout.SetColumnSpan(status, 3);
        var install = new TextBox { Dock = DockStyle.Fill };
        var user = new TextBox { Dock = DockStyle.Fill, Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime") };
        void FolderRow(int row, string title, TextBox field)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill }, 0, row); layout.Controls.Add(field, 1, row);
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill }; layout.Controls.Add(browse, 2, row);
            browse.Click += (_, _) => { using var dialog = new FolderBrowserDialog { Description = title, SelectedPath = field.Text }; if (dialog.ShowDialog(this) == DialogResult.OK) field.Text = dialog.SelectedPath; };
        }
        FolderRow(1, "小白安装目录", install); FolderRow(2, "小白用户数据", user);
        var apply = new Button { Text = "检测并继续", Dock = DockStyle.Fill }; layout.Controls.Add(apply, 1, 3);
        var cancel = new Button { Text = "取消", Dock = DockStyle.Fill, DialogResult = DialogResult.Cancel }; layout.Controls.Add(cancel, 2, 3);
        AcceptButton = apply; CancelButton = cancel;
        apply.Click += (_, _) =>
        {
            try
            {
                Result = PortableRuntime.Discover(root, string.IsNullOrWhiteSpace(install.Text) ? null : install.Text.Trim(), user.Text.Trim());
                File.WriteAllText(Path.Combine(root, "runtime-location.json"), JsonSerializer.Serialize(new { install = Result.InstallRoot, user = user.Text.Trim() }));
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception ex) { status.Text = ex.Message; }
        };
    }
}
