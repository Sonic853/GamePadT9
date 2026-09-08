using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GamePadT9;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox stick = new() { Name = "StickSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, ForeColor = Color.Black, BackColor = Color.White };
    private readonly ComboBox trigger = new() { Name = "TriggerSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, ForeColor = Color.Black, BackColor = Color.White };
    private readonly TrackBar[] sliders = new TrackBar[3];
    private readonly NumericUpDown[] percentages = new NumericUpDown[3];
    private readonly VisibilityPreview preview = new() { Dock = DockStyle.Fill, Height = 134 };
    private bool updating;
    internal UserSettings Draft => new()
    {
        Stick = (ControlSide)stick.SelectedIndex, Trigger = (ControlSide)trigger.SelectedIndex,
        PanelOpacity = (int)percentages[0].Value, GridOpacity = (int)percentages[1].Value, HighlightOpacity = (int)percentages[2].Value
    };
    internal SettingsForm(UserSettings settings, Action<UserSettings> save)
    {
        Text = "GamePad T9 · 设置"; Name = "SettingsWindow";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(600, 640); AutoScroll = true;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(20, 24, 29); ForeColor = Color.WhiteSmoke;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 640, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        foreach (var height in new[] { 40, 28, 90, 32, 30, 126, 148, 52, 46 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        Controls.Add(layout);
        layout.Controls.Add(Label("输入设置", 18, true), 0, 0);
        layout.Controls.Add(Label("手柄", 11, true), 0, 1);
        var bindings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        bindings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); bindings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bindings.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); bindings.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        stick.Items.AddRange(["左摇杆（按下为 L3）", "右摇杆（按下为 R3）"]);
        trigger.Items.AddRange(["左扳机 LT", "右扳机 RT"]);
        bindings.Controls.Add(Label("选择九宫格区域"), 0, 0); bindings.Controls.Add(stick, 1, 0);
        bindings.Controls.Add(Label("确认区域输入"), 0, 1); bindings.Controls.Add(trigger, 1, 1);
        layout.Controls.Add(bindings, 0, 2);
        layout.Controls.Add(Label("可见度", 11, true), 0, 3);
        layout.Controls.Add(Label("0% 完全透明，100% 完全不透明；文字保持清晰。", 9), 0, 4);
        var opacity = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        opacity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96)); opacity.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        opacity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68)); opacity.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 25));
        var names = new[] { "面板背景", "九宫格", "高亮区域" };
        for (var i = 0; i < 3; i++)
        {
            var index = i; opacity.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 3));
            sliders[i] = new TrackBar { Name = "OpacitySlider" + i, Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 10, Dock = DockStyle.Fill, AccessibleName = names[i] + "可见度" };
            percentages[i] = new NumericUpDown { Name = "OpacityValue" + i, Minimum = 0, Maximum = 100, Dock = DockStyle.Fill, AccessibleName = names[i] + "可见度百分比" };
            sliders[i].ValueChanged += (_, _) => { if (!updating) { percentages[index].Value = sliders[index].Value; Preview(); } };
            percentages[i].ValueChanged += (_, _) => { if (!updating) { sliders[index].Value = (int)percentages[index].Value; Preview(); } };
            opacity.Controls.Add(Label(names[i]), 0, i); opacity.Controls.Add(sliders[i], 1, i);
            opacity.Controls.Add(percentages[i], 2, i); opacity.Controls.Add(Label("%"), 3, i);
        }
        layout.Controls.Add(opacity, 0, 5); layout.Controls.Add(preview, 0, 6);
        layout.Controls.Add(Label("设置期间手柄输入已暂停。保存后，在目标文本框\n按 View + Menu 开启输入。", 9), 0, 7);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        var defaults = Button("恢复默认", "ResetDefaults"); var cancel = Button("取消", "CancelSettings"); var saveButton = Button("保存", "SaveSettings");
        saveButton.BackColor = Color.FromArgb(110, 236, 169); saveButton.ForeColor = Color.FromArgb(20, 24, 29);
        defaults.Click += (_, _) => SetDraft(new()); cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        saveButton.Click += (_, _) =>
        {
            try { var value = Draft; value.Validate(); save(value); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            { MessageBox.Show(this, "无法保存设置：" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        buttons.Controls.Add(defaults, 0, 0); buttons.Controls.Add(cancel, 2, 0); buttons.Controls.Add(saveButton, 3, 0);
        layout.Controls.Add(buttons, 0, 8); AcceptButton = saveButton; CancelButton = cancel;
        stick.SelectedIndexChanged += (_, _) => Preview(); trigger.SelectedIndexChanged += (_, _) => Preview();
        SetDraft(settings);
        Load += (_, _) => { Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height - 40); };
    }
    internal void SetDraft(UserSettings value)
    {
        updating = true;
        try
        {
            stick.SelectedIndex = (int)value.Stick; trigger.SelectedIndex = (int)value.Trigger;
            var values = new[] { value.PanelOpacity, value.GridOpacity, value.HighlightOpacity };
            for (var i = 0; i < 3; i++) { sliders[i].Value = values[i]; percentages[i].Value = values[i]; }
        }
        finally { updating = false; }
        Preview();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // The tray host may have been launched with STARTUPINFO requesting SW_HIDE.
        // An explicitly opened settings window must still become visible.
        ShowWindow(Handle, 1);
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(nint window, int command);
    private void Preview() { if (!updating) { preview.Settings = Draft; preview.Invalidate(); } }
    private static Label Label(string text, float size = 10, bool bold = false) => new()
    { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    private static Button Button(string text, string name) => new()
    { Text = text, Name = name, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33, 39, 46), Margin = new Padding(4) };

    private sealed class VisibilityPreview : Control
    {
        internal UserSettings Settings = new();
        internal VisibilityPreview() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var dark = new SolidBrush(Color.FromArgb(95, 109, 123));
            using var light = new SolidBrush(Color.FromArgb(170, 181, 192));
            for (var y = 0; y < Height; y += 16) for (var x = 0; x < Width; x += 16)
                e.Graphics.FillRectangle((x / 16 + y / 16) % 2 == 0 ? light : dark, x, y, 16, 16);
            using var surface = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(surface))
            {
                g.Clear(Color.FromArgb(UserSettings.Alpha(Settings.PanelOpacity), 20, 24, 29));
                using var grid = new SolidBrush(Color.FromArgb(UserSettings.Alpha(Settings.GridOpacity), 33, 39, 46));
                using var highlight = new SolidBrush(Color.FromArgb(UserSettings.Alpha(Settings.HighlightOpacity), 110, 236, 169));
                using var text = new SolidBrush(Color.White);
                for (var i = 0; i < 9; i++)
                {
                    var rect = new Rectangle(12 + i % 3 * 47, 10 + i / 3 * 43, 40, 36);
                    g.CompositingMode = CompositingMode.SourceCopy; g.FillRectangle(i == 4 ? highlight : grid, rect);
                    g.CompositingMode = CompositingMode.SourceOver;
                    using var label = new SolidBrush(i == 4 ? Color.FromArgb(20, 24, 29) : Color.White);
                    using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString((i + 1).ToString(), Font, label, rect, format);
                }
                // DrawString preserves alpha; opaque preview text stays readable.
                g.DrawString("效果预览", Font, text, 176, 22);
                g.DrawString($"{Settings.StickLabel}选区 · {Settings.TriggerLabel} 确认\n{Settings.StickClickLabel} 按下摇杆", Font, text, new RectangleF(176, 53, Width - 184, 70));
            }
            e.Graphics.DrawImageUnscaled(surface, 0, 0);
        }
    }
}
