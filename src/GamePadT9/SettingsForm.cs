using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GamePadT9;

internal sealed class SettingsForm : Form
{
    internal event Action? ProgramsRequested;
    private readonly ComboBox device = new() { Name = "ControllerSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, ForeColor = Color.Black, BackColor = Color.White, DropDownWidth = 560 };
    private readonly Label deviceStatus = Label("正在查找手柄…", 9);
    private readonly Func<IReadOnlyList<GamepadDevice>> connected;
    private readonly Func<string?> deviceError;
    private readonly ButtonGlyphs glyphs = new();
    private readonly PromptControl activation = new() { Dock = DockStyle.Fill };
    private IReadOnlyList<GamepadDevice>? lastDevices;
    private sealed record Choice(string? Id, string Name, GamepadFamily Family)
    { public override string ToString() => Name; }
    private Choice Selection => device.SelectedItem as Choice ?? new(null, "自动选择", GamepadFamily.Xbox);
    private GamepadFamily Family => Selection.Id == null ? connected().FirstOrDefault()?.Family ?? GamepadFamily.Xbox : Selection.Family;
    private readonly ComboBox stick = new() { Name = "StickSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, ForeColor = Color.Black, BackColor = Color.White };
    private readonly ComboBox trigger = new() { Name = "TriggerSelector", DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, ForeColor = Color.Black, BackColor = Color.White };
    private readonly TrackBar[] sliders = new TrackBar[3];
    private readonly NumericUpDown[] percentages = new NumericUpDown[3];
    private readonly VisibilityPreview preview = new() { Dock = DockStyle.Fill, Height = 134 };
    private bool updating;
    internal UserSettings Draft => new()
    {
        ControllerId = Selection.Id, ControllerName = Selection.Id == null ? null : Selection.Name.Replace("（未连接）", ""),
        ControllerFamily = Selection.Id == null ? GamepadFamily.Xbox : Selection.Family,
        Stick = (ControlSide)stick.SelectedIndex, Trigger = (ControlSide)trigger.SelectedIndex,
        PanelOpacity = (int)percentages[0].Value, GridOpacity = (int)percentages[1].Value, HighlightOpacity = (int)percentages[2].Value
    };
    internal SettingsForm(UserSettings settings, Action<UserSettings> save, Func<IReadOnlyList<GamepadDevice>>? connected = null, Func<string?>? deviceError = null, IntegrationInstaller? integration = null)
    {
        this.connected = connected ?? (() => Array.Empty<GamepadDevice>());
        this.deviceError = deviceError ?? (() => null);
        Text = "GamePad T9 · 设置"; Name = "SettingsWindow";
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen; ClientSize = new Size(620, 730); AutoScroll = true;
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(20, 24, 29); ForeColor = Color.WhiteSmoke;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 730, Padding = new Padding(24), ColumnCount = 1, RowCount = 9 };
        foreach (var height in new[] { 40, 28, 180, 32, 30, 126, 148, 52, 46 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        var tabs = new TabControl { Dock = DockStyle.Fill, Name = "SettingsTabs" };
        var inputPage = new TabPage("输入设置") { BackColor = BackColor, AutoScroll = true };
        var componentPage = new TabPage("输入法组件") { BackColor = BackColor, AutoScroll = true };
        tabs.TabPages.Add(inputPage); tabs.TabPages.Add(componentPage); Controls.Add(tabs);
        inputPage.Controls.Add(layout);
        AddComponentControls(componentPage, integration);
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 130));
        header.Controls.Add(Label("输入设置", 18, true), 0, 0);
        var programs = Button("程序列表…", "OpenProgramProfiles"); programs.Click += (_, _) => ProgramsRequested?.Invoke(); header.Controls.Add(programs, 1, 0);
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(Label("手柄", 11, true), 0, 1);
        var bindings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        bindings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145)); bindings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 43, 39, 49, 49 }) bindings.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        stick.Items.AddRange(["左摇杆", "右摇杆"]); trigger.Items.AddRange(["左扳机", "右扳机"]);
        StyleBinding(stick, true); StyleBinding(trigger, false);
        bindings.Controls.Add(Label("已连接手柄"), 0, 0); bindings.Controls.Add(device, 1, 0);
        bindings.Controls.Add(deviceStatus, 0, 1); bindings.SetColumnSpan(deviceStatus, 2);
        bindings.Controls.Add(Label("选择九宫格区域"), 0, 2); bindings.Controls.Add(stick, 1, 2);
        bindings.Controls.Add(Label("确认区域输入"), 0, 3); bindings.Controls.Add(trigger, 1, 3);
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
        layout.Controls.Add(activation, 0, 7);
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
        device.SelectedIndexChanged += (_, _) => { Preview(); UpdateDeviceStatus(); };
        SetDraft(settings);
        Load += (_, _) => { Height = Math.Min(Height, Screen.FromControl(this).WorkingArea.Height - 40); };
    }
    private void AddComponentControls(TabPage page, IntegrationInstaller? integration)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, Height = 350, Padding = new(22), RowCount = 5, ColumnCount = 1 };
        foreach (var height in new[] { 42, 78, 52, 52, 90 }) layout.RowStyles.Add(new(SizeType.Absolute, height));
        page.Controls.Add(layout);
        layout.Controls.Add(Label("输入法组件", 18, true), 0, 0);
        var status = Label("正在检测安装状态…"); status.Name = "ComponentStatus";
        var standalone = Button("注册 GamePad T9 输入法", "ToggleStandalone");
        var xiaobai = Button("注入小白 T9 输入", "ToggleXiaobai");
        layout.Controls.Add(status, 0, 1); layout.Controls.Add(standalone, 0, 2); layout.Controls.Add(xiaobai, 0, 3);
        layout.Controls.Add(Label("两种方式只能启用一种。注入会保留本机原版小白 DLL；还原后继续使用原版。操作后请重新打开目标程序。", 9), 0, 4);
        var operating = false;
        ComponentState? state = null;
        void RefreshState()
        {
            try
            {
                state = integration?.State();
                status.Text = state?.Description ?? "组件管理不可用。";
                standalone.Text = state?.Standalone == true ? "卸载 GamePad T9 输入法" : "注册 GamePad T9 输入法";
                xiaobai.Text = state?.Injected == true ? "还原小白 T9 输入" : "注入小白 T9 输入";
                standalone.Enabled = !operating && state != null && (state.Standalone || state.CanRegister);
                xiaobai.Enabled = !operating && state != null && (state.Injected || state.CanInject);
            }
            catch (Exception ex) { status.Text = ex.Message; standalone.Enabled = xiaobai.Enabled = false; }
        }
        async Task Run(ComponentAction action)
        {
            if (operating || integration == null) return;
            operating = true; RefreshState(); status.Text = "正在处理，请完成 Windows 管理员授权…";
            try
            {
                var result = await integration.RequestAsync(action);
                RefreshState(); status.Text = result.Message + "\n" + (state?.Description ?? "");
            }
            catch (Exception ex) { status.Text = "操作未完成：" + ex.Message; }
            finally
            {
                operating = false;
                standalone.Enabled = state != null && (state.Standalone || state.CanRegister);
                xiaobai.Enabled = state != null && (state.Injected || state.CanInject);
            }
        }
        standalone.Click += async (_, _) => await Run(state?.Standalone == true ? ComponentAction.UnregisterStandalone : ComponentAction.RegisterStandalone);
        xiaobai.Click += async (_, _) => await Run(state?.Injected == true ? ComponentAction.RestoreXiaobai : ComponentAction.InjectXiaobai);
        page.Enter += (_, _) => { if (!operating) RefreshState(); };
        FormClosing += (_, e) => { if (operating) e.Cancel = true; };
        RefreshState();
    }
    internal void SetDraft(UserSettings value)
    {
        updating = true;
        try
        {
            RebuildDevices(value.ControllerId, value.ControllerName, value.ControllerFamily);
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
    private void Preview()
    {
        if (updating) return;
        preview.Settings = Draft; preview.Family = Family; preview.Invalidate();
        activation.Family = Family; activation.Invalidate(); stick.Invalidate(); trigger.Invalidate();
    }
    internal void RefreshDevices()
    {
        var devices = connected();
        if (lastDevices == null || !devices.SequenceEqual(lastDevices))
        {
            var current = Selection;
            var wasUpdating = updating; updating = true;
            try { RebuildDevices(current.Id, current.Name.Replace("（未连接）", ""), current.Family); }
            finally { updating = wasUpdating; }
            Preview();
        }
        UpdateDeviceStatus();
    }
    private void RebuildDevices(string? id, string? name, GamepadFamily family)
    {
        lastDevices = connected().ToArray();
        device.BeginUpdate();
        try
        {
            device.Items.Clear(); device.Items.Add(new Choice(null, "自动选择（保持当前手柄）", GamepadFamily.Xbox));
            foreach (var pad in lastDevices) device.Items.Add(new Choice(pad.Id, $"{pad.Name} · #{pad.Instance}", pad.Family));
            if (id != null && !lastDevices.Any(pad => pad.Id == id)) device.Items.Add(new Choice(id, (name ?? "已保存的手柄") + "（未连接）", family));
            device.SelectedItem = device.Items.Cast<Choice>().First(item => item.Id == id);
        }
        finally { device.EndUpdate(); }
        UpdateDeviceStatus();
    }
    private void UpdateDeviceStatus()
    {
        var error = deviceError();
        deviceStatus.Text = error != null ? "手柄读取失败：" + error :
            Selection.Id != null && !connected().Any(p => p.Id == Selection.Id) ? "所选手柄未连接，输入将保持关闭并等待重连。" :
            connected().Count == 0 ? "未发现手柄；请通过 USB 或蓝牙连接。" : $"已连接 {connected().Count} 个手柄；列表会自动更新。";
    }
    private void StyleBinding(ComboBox control, bool isStick)
    {
        control.DrawMode = DrawMode.OwnerDrawFixed; control.ItemHeight = 32;
        control.BackColor = Color.FromArgb(33, 39, 46); control.ForeColor = Color.White;
        control.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(47, 69, 63) : control.BackColor);
            e.Graphics.FillRectangle(background, e.Bounds);
            var left = e.Index == 0;
            var prompt = isStick ? (left ? "[LS] 左摇杆 · [L3] 按下确认" : "[RS] 右摇杆 · [R3] 按下确认") : (left ? "[LT] 左扳机" : "[RT] 右扳机");
            glyphs.Draw(e.Graphics, prompt, Family, Font, Color.White, new(e.Bounds.X + 5, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), 26);
            e.DrawFocusRectangle();
        };
    }
    protected override void Dispose(bool disposing) { if (disposing) glyphs.Dispose(); base.Dispose(disposing); }
    private static Label Label(string text, float size = 10, bool bold = false) => new()
    { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular) };
    private static Button Button(string text, string name) => new()
    { Text = text, Name = name, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(33, 39, 46), Margin = new Padding(4) };

    private sealed class VisibilityPreview : Control
    {
        internal UserSettings Settings = new();
        internal GamepadFamily Family;
        private readonly ButtonGlyphs glyphs = new();
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
                glyphs.Draw(g, $"[{(Settings.Stick == ControlSide.Left ? "LS" : "RS")}] 选区  [{Settings.TriggerLabel}] 确认", Family, Font, Color.White, new(176, 53, Width - 184, 30), 26);
                glyphs.Draw(g, $"[{Settings.StickClickLabel}] 按下摇杆", Family, Font, Color.White, new(176, 88, Width - 184, 30), 26);
            }
            e.Graphics.DrawImageUnscaled(surface, 0, 0);
        }
        protected override void Dispose(bool disposing) { if (disposing) glyphs.Dispose(); base.Dispose(disposing); }
    }
    private sealed class PromptControl : Control
    {
        internal GamepadFamily Family;
        private readonly ButtonGlyphs glyphs = new();
        internal PromptControl() { DoubleBuffered = true; AccessibleName = "保存后，在目标文本框同时按下两枚菜单键开启输入"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            glyphs.Draw(e.Graphics, "设置期间输入已暂停。保存后，按 [View] + [Menu] 开启输入", Family, Font, ForeColor, new(0, 0, Width, Height), 26);
        }
        protected override void Dispose(bool disposing) { if (disposing) glyphs.Dispose(); base.Dispose(disposing); }
    }
}
