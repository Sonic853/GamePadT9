using System.Drawing;
using System.Windows.Forms;

namespace GamePadT9;

internal sealed class GamePadApplication : ApplicationContext
{
    private readonly InputSession session;
    private readonly MainForm overlay;
    private readonly Controller controller = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem toggle;
    private uint? slot;
    private long focusCheck;
    public GamePadApplication(RimeEngine engine)
    {
        session = new(engine); overlay = new(session);
        var menu = new ContextMenuStrip();
        toggle = new("开启输入", null, (_, _) => session.Enable(!session.Enabled));
        menu.Items.Add(toggle);
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "GamePad T9 · View + Menu 开启", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => session.Enable(!session.Enabled);
        session.Changed += () => { toggle.Text = session.Enabled ? "关闭输入" : "开启输入"; overlay.Present(controller.Region, slot); };
        timer.Tick += Tick;
        timer.Start();
    }
    private async void Tick(object? sender, EventArgs e)
    {
        PadState state = default;
        if (slot is uint index && !Controller.Read(index, out state))
        { slot = null; controller.Reset(); session.Enable(false); }
        if (slot == null) for (uint i = 0; i < 4; i++) if (Controller.Read(i, out state)) { slot = i; break; }
        if (slot != null)
            foreach (var action in controller.Update(state.Pad, Environment.TickCount64)) await session.Handle(action);
        if (Environment.TickCount64 - focusCheck >= 250)
        { focusCheck = Environment.TickCount64; session.CheckFocus(); }
        overlay.Present(controller.Region, slot);
    }
    protected override void ExitThreadCore()
    {
        timer.Stop(); tray.Visible = false; overlay.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { timer.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); overlay.Dispose(); }
        base.Dispose(disposing);
    }
}
