using System.Runtime.InteropServices;

namespace GamePadT9;

// A composition-only surface behind the alpha window. It never owns focus or
// receives input; the existing WPF / WinForms surfaces keep handling all input.
internal sealed class BlurBackdrop : Form
{
    private nint native;
    private float radius = -1;
    internal string? Error { get; private set; }
    internal static bool Supported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x00200000 | 0x08000000 | 0x00000020 | 0x00000080 | 0x00000008; return cp; }
    }
    internal BlurBackdrop()
    {
        Text = "GamePad T9 · 背景效果";
        // Set topmost through the native style / z-order. Form.TopMost makes
        // WinForms call SetFocus after Show(), even with ShowWithoutActivation.
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        SetStyle(ControlStyles.Selectable, false);
        StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
    }
    internal void Present(nint owner, Rectangle bounds, int amount, bool visible)
    {
        if (IsDisposed || Disposing) return;
        if (!visible || amount <= 0 || !Supported || Error != null) { if (Visible) Hide(); return; }
        try
        {
            var blurRadius = amount * .5f * Math.Max(96, GetDpiForWindow(owner)) / 96f;
            if (native == 0)
            {
                System.Windows.Forms.Application.OleRequired();
                Marshal.ThrowExceptionForHR(BackdropCreate(Handle, blurRadius, out native));
                radius = blurRadius;
            }
            if (radius != blurRadius) { Marshal.ThrowExceptionForHR(BackdropSetBlur(native, blurRadius)); radius = blurRadius; }
            Bounds = bounds;
            if (!Visible) Show();
            SetWindowPos(Handle, owner, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040);
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        { Error = "背景模糊不可用：" + ex.Message; Hide(); System.Diagnostics.Debug.WriteLine(Error); }
    }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084) { message.Result = new nint(-1); return; } // HTTRANSPARENT
        if (message.Msg == 0x0021) { message.Result = new nint(3); return; } // MA_NOACTIVATE
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing)
    {
        if (native != 0) { BackdropDestroy(native); native = 0; }
        base.Dispose(disposing);
    }
    [DllImport("GamePadT9.Backdrop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int BackdropCreate(nint window, float amount, out nint instance);
    [DllImport("GamePadT9.Backdrop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int BackdropSetBlur(nint instance, float amount);
    [DllImport("GamePadT9.Backdrop.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void BackdropDestroy(nint instance);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
