using System.Drawing;
using System.Windows.Forms;

namespace GamePadT9;

internal sealed class MainForm : Form
{
    private readonly RimeEngine engine;
    private readonly TsfClient tsf = new();
    private readonly Controller controller = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Label status = new() { AutoSize = false, Height = 60, Dock = DockStyle.Top };
    private readonly Label preedit = new() { AutoSize = false, Height = 48, Dock = DockStyle.Top };
    private readonly Label candidates = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly Label[] regions = new Label[9];
    private readonly CheckBox enabledBox = new() { Text = "开启手柄输入（View + Menu）", AutoSize = true };
    private readonly Label connection = new() { AutoSize = true };
    private bool enabled, busy;
    private uint? padIndex;
    private Target? boundTarget;
    private long lastFocusCheck;
    private string message = "在记事本切换到「GamePad T9 验证」，再开启手柄输入。";
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var value = base.CreateParams; value.ExStyle |= 0x08000000 | 0x00000080; return value; }
    }
    public MainForm(RimeEngine engine)
    {
        this.engine = engine;
        Text = "GamePad T9 · 最小验证";
        Font = new Font("Microsoft YaHei UI", 11);
        ClientSize = new Size(470, 650); TopMost = true;
        StartPosition = FormStartPosition.Manual;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 24, area.Top + 80);
        BackColor = Color.FromArgb(25, 29, 35); ForeColor = Color.WhiteSmoke;
        Padding = new Padding(16);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.Controls.Add(enabledBox, 0, 0); layout.Controls.Add(connection, 0, 1);
        var grid = new TableLayoutPanel { ColumnCount = 3, RowCount = 3, Dock = DockStyle.Fill };
        for (var i = 0; i < 3; i++) { grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f)); grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333f)); }
        string[] names = ["符号", "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ"];
        for (var i = 0; i < 9; i++)
        {
            regions[i] = new Label { Text = names[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(3), BackColor = Color.FromArgb(45, 51, 60), Font = new Font(Font, FontStyle.Bold) };
            grid.Controls.Add(regions[i], i % 3, i / 3);
        }
        layout.Controls.Add(grid, 0, 2); layout.Controls.Add(preedit, 0, 3); layout.Controls.Add(candidates, 0, 4);
        layout.Controls.Add(status, 0, 5);
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "RT / R3 输入 · A 选词 · LB / RB 翻选\nX 删编码 · B 取消 · 长按 B 关闭 · 十字键左右翻页", Font = new Font(Font.FontFamily, 9) }, 0, 6);
        Controls.Add(layout);
        enabledBox.CheckedChanged += (_, _) => SetEnabled(enabledBox.Checked);
        timer.Tick += Tick;
        Shown += (_, _) => timer.Start();
        FormClosed += (_, _) => { timer.Stop(); timer.Dispose(); };
        Render();
    }
    private void SetEnabled(bool value)
    {
        if (busy) { enabledBox.Checked = enabled; return; }
        enabled = value; enabledBox.Checked = value;
        engine.Clear(); boundTarget = null;
        message = value ? "已开启，右摇杆选区，RT / R3 输入。" : "手柄输入已关闭。";
        Render();
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (busy) return;
        try
        {
            PadState state = default;
            if (padIndex is uint index && !Controller.Read(index, out state))
            {
                padIndex = null; controller.Reset(); SetEnabled(false); message = "手柄已断开；重新连接后需重新开启。";
            }
            if (padIndex == null)
            {
                for (uint i = 0; i < 4; i++) if (Controller.Read(i, out state)) { padIndex = i; break; }
            }
            connection.Text = padIndex is uint p ? $"Xbox 手柄 {p + 1} 已连接" : "等待 Xbox / XInput 手柄连接";
            if (padIndex != null)
                foreach (var action in controller.Update(state.Pad, Environment.TickCount64)) await Act(action);
            if (Environment.TickCount64 - lastFocusCheck > 250)
            {
                lastFocusCheck = Environment.TickCount64;
                if (boundTarget is Target target && engine.PendingCommit.Length == 0 && TsfClient.FindTarget() != target)
                { engine.Clear(); boundTarget = null; message = "输入焦点已变化，已取消未提交的编码。"; }
            }
            Render();
        }
        catch (Exception ex) { message = ex.Message; Render(); }
    }
    private async Task Act(PadEvent action)
    {
        if (action.Action == PadAction.Toggle) { SetEnabled(!enabled); return; }
        if (action.Action == PadAction.Disable) { SetEnabled(false); return; }
        if (!enabled) return;
        if (action.Action == PadAction.Cancel) { engine.Clear(); boundTarget = null; message = "已取消当前输入。"; return; }
        if (engine.PendingCommit.Length != 0) { message = "存在未确认的提交，请检查文本框后按 B 清除。"; return; }
        var target = TsfClient.FindTarget();
        if (target == null) { message = "请在可编辑文本框中选择「GamePad T9 验证」输入法。"; return; }
        if (boundTarget != null && boundTarget != target) { engine.Clear(); boundTarget = null; }
        if (action.Action == PadAction.Region) { boundTarget = target; engine.InputRegion(action.Region); }
        else if (boundTarget != null)
        {
            switch (action.Action)
            {
                case PadAction.Confirm: engine.Confirm(); break;
                case PadAction.Backspace: engine.Process(0xFF08); break;
                case PadAction.Previous: engine.Process(0xFF52); break;
                case PadAction.Next: engine.Process(0xFF54); break;
                case PadAction.PagePrevious: engine.Process(0xFF55); break;
                case PadAction.PageNext: engine.Process(0xFF56); break;
            }
        }
        message = "九键输入中";
        if (engine.PendingCommit.Length > 0 && boundTarget is Target commitTarget)
        {
            busy = true; enabledBox.Enabled = false;
            try
            {
                var text = engine.PendingCommit;
                var error = await tsf.Commit(commitTarget, text);
                if (error == null) { engine.AcknowledgeCommit(); message = $"TSF 已确认上屏：{text}"; }
                else message = error;
            }
            finally { busy = false; enabledBox.Enabled = true; }
        }
        if (engine.View.Preedit.Length == 0 && engine.PendingCommit.Length == 0) boundTarget = null;
    }
    private void Render()
    {
        for (var i = 0; i < 9; i++) regions[i].BackColor = i == controller.Region ? Color.FromArgb(38, 121, 85) : Color.FromArgb(45, 51, 60);
        preedit.Text = "编码：" + (engine.View.Preedit.Length == 0 ? "—" : engine.View.Preedit);
        candidates.Text = string.Join("\n", engine.View.Candidates.Select((c, i) => $"{(i == engine.View.Highlight ? "▶" : "  ")} {i + 1}. {c.Text}  {c.Comment}"));
        status.Text = message;
    }
}
