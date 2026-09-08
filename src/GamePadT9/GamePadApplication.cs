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
    private readonly InputMethodSwitcher inputMethods;
    private uint? slot;
    private long focusCheck;
    private bool ticking, exiting;
    public GamePadApplication(RimeEngine engine, string root)
    {
        inputMethods = new(root);
        session = new(engine, inputMethods); overlay = new(session);
        var menu = new ContextMenuStrip();
        toggle = new("开启输入", null, async (_, _) => await ToggleFromTray());
        menu.Items.Add(toggle);
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "GamePad T9 · View + Menu 开启", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += async (_, _) => await ToggleFromTray();
        session.Error += message => tray.ShowBalloonTip(5000, "GamePad T9", message, ToolTipIcon.Warning);
        session.Changed += () => { toggle.Text = session.Enabled ? "关闭输入" : "开启输入"; overlay.Present(controller.Region, slot); };
        timer.Tick += Tick;
        timer.Start();
    }
    private async Task ToggleFromTray()
    {
        if (!session.Enabled && !session.Busy)
        {
            await Task.Delay(50); // Let the tray menu close before returning focus.
            inputMethods.FocusLastWindow();
        }
        await session.Handle(new(PadAction.Toggle));
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (ticking || exiting) return;
        ticking = true;
        try
        {
            inputMethods.ObserveForeground();
            PadState state = default;
            if (slot is uint index && !Controller.Read(index, out state))
            { slot = null; controller.Reset(); await session.Enable(false); }
            if (slot == null) for (uint i = 0; i < 4; i++) if (Controller.Read(i, out state)) { slot = i; break; }
            if (slot != null)
                foreach (var action in controller.Update(state.Pad, Environment.TickCount64)) await session.Handle(action);
            if (Environment.TickCount64 - focusCheck >= 250)
            { focusCheck = Environment.TickCount64; await session.CheckFocus(); }
            overlay.Present(controller.Region, slot);
        }
        finally { ticking = false; }
    }
    protected override async void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true; timer.Stop();
        await session.ShutdownAsync();
        tray.Visible = false; overlay.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            // Covers WM_QUIT / orderly message-loop shutdown as well as the tray path.
            if (inputMethods.HasSavedProfiles)
                try { inputMethods.RestoreAsync().GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            timer.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); overlay.Dispose();
        }
        base.Dispose(disposing);
    }
}
