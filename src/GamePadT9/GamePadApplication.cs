using System.Drawing;
using System.Windows.Forms;

namespace GamePadT9;

internal sealed class GamePadApplication : ApplicationContext
{
    private readonly InputSession session;
    private readonly MainForm overlay;
    private readonly Controller controller = new();
    private readonly GamepadDevices devices = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly NotifyIcon tray;
    private readonly ToolStripMenuItem toggle;
    private readonly InputMethodSwitcher inputMethods;
    private readonly string root;
    private UserSettings preferences;
    private SettingsForm? settingsForm;
    private ProgramProfilesForm? profilesForm;
    private ProgramProfiles profiles;
    private Gamepad latestState;
    private bool deviceConnected;
    private bool openingProfiles;
    private uint? activeInstance;
    private long focusCheck;
    private bool ticking, exiting, openingSettings;
    public GamePadApplication(RimeEngine engine, string root)
    {
        this.root = root;
        preferences = UserSettings.Load(root, out var warning);
        profiles = ProgramProfiles.Load(root, out var profileWarning);
        warning ??= profileWarning;
        inputMethods = new(root);
        session = new(engine, inputMethods) { Preferences = preferences }; overlay = new(session);
        session.ControlsReleased = () => !deviceConnected || Controller.IsNeutral(latestState);
        session.ButtonsReleased = () => !deviceConnected || Controller.AreButtonsReleased(latestState);
        session.OverlayBounds = () => overlay.Bounds;
        overlay.LocationChanged += (_, _) => { if (session.Focused is { Enabled: true, External: true } focused) focused.Form.PlaceAbove(overlay.Bounds); };
        session.FocusConfiguration = () =>
        {
            var window = InputMethodSwitcher.Foreground() ?? throw new InvalidOperationException("请先选中目标程序，再开启输入。");
            var path = ProgramProfiles.Executable(window);
            return (profiles.Resolve(path), window, path ?? $"process:{window.Process}");
        };
        controller.Configure(preferences);
        overlay.SettingsRequested += async () => await OpenSettings();
        var menu = new ContextMenuStrip();
        toggle = new("开启输入", null, async (_, _) => await ToggleFromTray());
        menu.Items.Add(toggle);
        menu.Items.Add("设置…", null, async (_, _) => await OpenSettings());
        menu.Items.Add("程序列表…", null, async (_, _) => await OpenProfiles());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "GamePad T9 · 双菜单键开启", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += async (_, _) => await ToggleFromTray();
        session.Error += message => tray.ShowBalloonTip(5000, "GamePad T9", message, ToolTipIcon.Warning);
        session.Changed += () => { controller.ConfigureCompletion(session.Enabled && session.ExternalInput); toggle.Text = session.Enabled ? "关闭输入" : "开启输入"; overlay.Present(controller.Region, activeInstance); };
        timer.Tick += Tick;
        timer.Start();
        if (warning != null) tray.ShowBalloonTip(5000, "GamePad T9", warning, ToolTipIcon.Warning);
    }
    internal async Task OpenSettings()
    {
        if (exiting || openingSettings) return;
        if (settingsForm != null) { settingsForm.Activate(); return; }
        openingSettings = true;
        try
        {
            await session.ShutdownAsync();
            if (exiting) return;
            controller.Reset();
            var form = new SettingsForm(preferences, value =>
            {
                value.Save(root); preferences = value;
                controller.Reset(); controller.Configure(value); overlay.ApplySettings(value);
            }, () => devices.Connected, () => devices.Error);
            form.ProgramsRequested += async () => await OpenProfiles();
            settingsForm = form;
            form.FormClosed += (_, _) => { settingsForm = null; controller.Reset(); form.Dispose(); };
            form.Show(); form.Activate();
        }
        finally { openingSettings = false; }
    }
    internal async Task OpenProfiles()
    {
        if (exiting || openingProfiles) return;
        if (profilesForm != null) { profilesForm.Activate(); return; }
        openingProfiles = true;
        try
        {
            await session.ShutdownAsync(); controller.Reset();
            if (exiting) return;
            var form = new ProgramProfilesForm(profiles, value => { value.Save(root); profiles = value; });
            profilesForm = form;
            form.FormClosed += (_, _) => { profilesForm = null; controller.Reset(); form.Dispose(); };
            form.Show(); form.Activate();
        }
        finally { openingProfiles = false; }
    }
    private async Task ToggleFromTray()
    {
        if (openingSettings || openingProfiles || settingsForm != null || profilesForm != null) return;
        if (!session.Enabled && !session.Busy)
        {
            await Task.Delay(50); // Let the tray menu close before returning focus.
            inputMethods.FocusLastWindow();
        }
        await session.Handle(new(PadAction.Toggle));
    }
    private async void Tick(object? sender, EventArgs e)
    {
        if (exiting) return;
        // Keep sampling while a submission waits for controls to be released.
        var connected = devices.Poll(preferences.ControllerId, out var state);
        latestState = state; deviceConnected = connected;
        if (ticking)
        {
            if (!connected || devices.Active?.Instance != activeInstance) { await session.Enable(false); return; }
            // Only cancellation is meaningful while an edit is in flight; never queue stale typing.
            foreach (var action in controller.Update(state, Environment.TickCount64))
                if (action.Action is PadAction.Disable or PadAction.Toggle) await session.Enable(false);
            return;
        }
        ticking = true;
        try
        {
            overlay.UseDevice(devices.Active);
            if (session.Focused is { } focused) focused.Form.UseFamily(devices.Active?.Family ?? preferences.ControllerFamily);
            if (activeInstance != devices.Active?.Instance)
            {
                var wasConnected = activeInstance != null;
                activeInstance = devices.Active?.Instance; controller.Reset();
                if (wasConnected) await session.ShutdownAsync();
            }
            settingsForm?.RefreshDevices();
            if (openingSettings || openingProfiles || settingsForm != null || profilesForm != null) return;
            inputMethods.ObserveForeground();
            if (connected)
                foreach (var action in controller.Update(state, Environment.TickCount64)) await session.Handle(action);
            if (Environment.TickCount64 - focusCheck >= 250)
            { focusCheck = Environment.TickCount64; await session.CheckFocus(); }
            overlay.Present(controller.Region, activeInstance);
        }
        finally { ticking = false; }
    }
    protected override async void ExitThreadCore()
    {
        if (exiting) return;
        exiting = true; timer.Stop();
        settingsForm?.Close();
        profilesForm?.Close();
        await session.ShutdownAsync();
        tray.Visible = false; overlay.Close();
        base.ExitThreadCore();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            timer.Stop();
            settingsForm?.Dispose();
            profilesForm?.Dispose();
            // Covers WM_QUIT / orderly message-loop shutdown as well as the tray path.
            if (inputMethods.HasSavedProfiles)
                try { inputMethods.RestoreAsync().GetAwaiter().GetResult(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            session.Dispose(); timer.Dispose(); tray.ContextMenuStrip?.Dispose(); tray.Dispose(); overlay.Dispose(); devices.Dispose();
        }
        base.Dispose(disposing);
    }
}
