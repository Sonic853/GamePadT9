using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace GamePadT9;

internal sealed class MainForm : Form
{
    private const int DesignWidth = 780, DesignHeight = 520;
    private readonly InputSession session;
    private int region = 4;
    private uint? pad;
    private float scale = 1;
    private bool dragging;
    private Point dragOrigin, windowOrigin;
    private readonly Font titleFont = new("Microsoft YaHei UI", 19, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font mainFont = new("Microsoft YaHei UI", 19, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font gridFont = new("Microsoft YaHei UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font smallFont = new("Microsoft YaHei UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Color Background = Color.FromArgb(20, 24, 29);
    private static readonly Color PanelColor = Color.FromArgb(33, 39, 46);
    private static readonly Color Accent = Color.FromArgb(110, 236, 169);
    private static readonly Color Muted = Color.FromArgb(153, 166, 180);
    private readonly RectangleF modeRect = new(586, 18, 130, 36), closeRect = new(728, 18, 32, 36);
    private long lastRaise;
    internal int DisplayedCandidateCount => session.View.Candidates.Length;
    internal int HighlightedRegion => region;
    internal InputMode DisplayedMode => session.Mode;
    internal string DisplayedPreedit => string.Create(session.View.Preedit.Length, session.View.Preedit, static (output, original) =>
    {
        // Render the original 789/456/123 schema codes as the controller's 123/456/789.
        for (var i = 0; i < original.Length; i++) output[i] = original[i] switch
        { '7' => '1', '8' => '2', '9' => '3', '1' => '7', '2' => '8', '3' => '9', _ => original[i] };
    });
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000008; return cp; }
    }
    public MainForm(InputSession session)
    {
        this.session = session;
        Text = "GamePad T9 · 输入面板";
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Background; DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.Manual; ClientSize = new Size(DesignWidth, DesignHeight);
        session.Changed += OnSessionChanged;
    }
    private void OnSessionChanged() { if (!IsDisposed) { Present(region, pad); Invalidate(); } }
    internal void Present(int selectedRegion, uint? controllerSlot)
    {
        var changed = region != selectedRegion || pad != controllerSlot;
        region = selectedRegion; pad = controllerSlot;
        if (!session.Enabled) { if (Visible) Hide(); return; }
        if (!Visible)
        {
            var area = Screen.FromHandle(TsfClient.GetForegroundWindow()).WorkingArea;
            // Fit the monitor's physical work area even at large display scaling.
            scale = Math.Min(DeviceDpi / 96f, Math.Min((area.Width - 32f) / DesignWidth, (area.Height - 32f) / DesignHeight));
            scale = Math.Max(0.5f, scale);
            ClientSize = new Size((int)(DesignWidth * scale), (int)(DesignHeight * scale));
            Location = new Point(area.Right - Width - 20, area.Bottom - Height - 20);
            Show(); changed = true;
        }
        if (changed) Invalidate();
        if (Environment.TickCount64 - lastRaise >= 250)
        {
            lastRaise = Environment.TickCount64;
            SetWindowPos(Handle, new nint(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 | 0x0200);
        }
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0021) { m.Result = new nint(3); return; } // MA_NOACTIVATE
        base.WndProc(ref m);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.ScaleTransform(scale, scale); g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        TextAt(g, "GAMEPAD T9", titleFont, Color.White, new(20, 20, 270, 30));
        TextAt(g, pad is uint n ? $"●  手柄 {n + 1} 已连接" : "●  等待手柄连接", smallFont, pad == null ? Muted : Accent, new(22, 57, 300, 25));
        Fill(g, modeRect, PanelColor);
        TextAt(g, session.Mode == InputMode.T9 ? "Y   九键中文" : "Y   数字输入", smallFont, Accent, modeRect, true);
        TextAt(g, "×", gridFont, Muted, closeRect, true);
        string[] labels = session.Mode == InputMode.T9 ? ["符号", "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ"] : ["1", "2", "3", "4", "5", "6", "7", "8", "9"];
        for (var i = 0; i < 9; i++)
        {
            var rect = Cell(i); var selected = i == region;
            Fill(g, rect, selected ? Accent : PanelColor);
            TextAt(g, labels[i], gridFont, selected ? Background : Color.White, rect, true);
        }
        TextAt(g, session.Mode == InputMode.T9 ? "RT / R3  输入高亮区域" : "RT  输入高亮数字    R3  输入 0", smallFont, Muted, new(20, 420, 335, 25), true);
        if (session.Mode == InputMode.Numeric)
        {
            TextAt(g, "数字输入", titleFont, Color.White, new(366, 96, 360, 28));
            TextAt(g, "最近输入", smallFont, Muted, new(366, 150, 360, 24));
            TextAt(g, session.NumberHistory.Length == 0 ? "—" : session.NumberHistory, gridFont, Accent, new(366, 186, 394, 90));
            TextAt(g, "R3 输入 0\nX 退格\nY 返回九键中文", mainFont, Muted, new(366, 294, 394, 115));
        }
        else
        {
            TextAt(g, "候选词", titleFont, Color.White, new(366, 96, 200, 28));
            var view = session.View;
            TextAt(g, $"第 {view.Page + 1} 页", smallFont, Muted, new(676, 101, 84, 24));
            Fill(g, new(366, 134, 394, 35), PanelColor);
            TextAt(g, view.Preedit.Length == 0 ? "选择字母组，按 RT 开始输入" : DisplayedPreedit, smallFont, Accent, new(378, 141, 370, 23));
            if (view.Candidates.Length == 0)
                TextAt(g, "候选词将在这里显示\n\nA 确认当前候选\nLB / RB 上下选择\n十字键左右翻页", mainFont, Muted, new(380, 205, 360, 190));
            for (var i = 0; i < Math.Min(view.Candidates.Length, 9); i++)
            {
                var rect = CandidateRect(i); var selected = i == view.Highlight;
                if (selected) Fill(g, rect, Accent);
                var text = view.Candidates[i];
                TextAt(g, (i + 1).ToString(), smallFont, selected ? Background : Muted, new(rect.X + 10, rect.Y + 5, 24, 24));
                TextAt(g, text.Text, mainFont, selected ? Background : Color.White, new(rect.X + 38, rect.Y + 1, 210, 27));
                TextAt(g, text.Comment, smallFont, selected ? Color.FromArgb(43, 87, 62) : Muted, new(rect.X + 252, rect.Y + 5, 132, 22));
            }
        }
        using var line = new Pen(Color.FromArgb(49, 57, 65)); g.DrawLine(line, 20, 454, 760, 454);
        TextAt(g, session.Busy ? "正在输入…" : session.Message, smallFont, Color.WhiteSmoke, new(20, 466, 740, 22));
        TextAt(g, "A 选词   LB / RB 翻选   X 退格   B 关闭候选   长按 B 关闭输入   View + Menu 开关", smallFont, Muted, new(20, 494, 740, 22));
        using var border = new Pen(Color.FromArgb(70, 85, 96)); g.DrawRectangle(border, 0, 0, DesignWidth - 1, DesignHeight - 1);
    }
    private static RectangleF Cell(int i) => new(20 + i % 3 * 108, 96 + i / 3 * 108, 100, 100);
    private static RectangleF CandidateRect(int i) => new(366, 181 + i * 29, 394, 28);
    private static void Fill(Graphics g, RectangleF rect, Color color) { using var brush = new SolidBrush(color); g.FillRectangle(brush, rect); }
    private static void TextAt(Graphics g, string text, Font font, Color color, RectangleF rect, bool center = false)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat { Alignment = center ? StringAlignment.Center : StringAlignment.Near, LineAlignment = center ? StringAlignment.Center : StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        g.DrawString(text, font, brush, rect, format);
    }
    protected override async void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var point = new PointF(e.X / scale, e.Y / scale);
        if (closeRect.Contains(point)) { await session.Enable(false); return; }
        if (modeRect.Contains(point)) { await session.Handle(new(PadAction.SwitchMode)); return; }
        if (point.Y < 82) { dragging = true; dragOrigin = Cursor.Position; windowOrigin = Location; Capture = true; return; }
        for (var i = 0; i < 9; i++)
            if (Cell(i).Contains(point)) { await session.Handle(new(PadAction.Region, i)); return; }
        if (session.Mode == InputMode.T9)
            for (var i = 0; i < Math.Min(session.View.Candidates.Length, 9); i++)
                if (CandidateRect(i).Contains(point)) { await session.Handle(new(PadAction.Confirm), i); return; }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) Location = new Point(windowOrigin.X + Cursor.Position.X - dragOrigin.X, windowOrigin.Y + Cursor.Position.Y - dragOrigin.Y);
    }
    protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { session.Changed -= OnSessionChanged; titleFont.Dispose(); mainFont.Dispose(); gridFont.Dispose(); smallFont.Dispose(); }
        base.Dispose(disposing);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
}
