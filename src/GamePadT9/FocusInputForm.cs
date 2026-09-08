using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;

namespace GamePadT9;

internal sealed partial class FocusInputForm : PanelWindow
{
    internal const double DesignHeight = 112;
    private readonly SteamGlyph glyph = new() { Token = "A", Width = 22, Height = 22 };
    private bool busy;
    internal bool AllowClose;
    internal nint ExistingHandle => new WindowInteropHelper(this).Handle;
    internal event Action? CompleteRequested, CancelRequested, CopyRequested, ClearRequested;

    internal FocusInputForm(bool external, CompletionDestination destination, string program)
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // Disable Windows' show/hide transitions for this input window only.
            var disabled = 1;
            var result = DwmSetWindowAttribute(Handle, 3 /* DWMWA_TRANSITIONS_FORCEDISABLED */, ref disabled, sizeof(int));
            if (result < 0) System.Diagnostics.Debug.WriteLine($"Disable external window transitions: 0x{result:X8}");
        };
        Title += " · " + program;
        TargetProgram.Text = program; TargetProgram.ToolTip = program;
        var clipboard = destination == CompletionDestination.Clipboard;
        DestinationLabel.Text = clipboard ? "完成后复制到剪贴板" : "完成后填入目标程序";
        CompletionLabel.Text = clipboard ? "完成并复制" : "完成并填入";
        AutomationProperties.SetName(CompleteButton, CompletionLabel.Text + "，长按确认键");
        CompleteButton.ToolTip = "点击此按钮，或长按手柄确认键 1 秒";
        CompletionGlyph.Content = glyph;
        CompleteButton.Visibility = external ? Visibility.Visible : Visibility.Collapsed;
        Editor.IsReadOnly = !external;
        Editor.TextChanged += (_, _) => RefreshDraftState();
        RefreshDraftState();
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; CancelRequested?.Invoke(); } };
    }

    internal void UpdateState(string message, bool editable, bool canComplete, bool isBusy)
    {
        busy = isBusy;
        Status.Text = message; Status.ToolTip = message;
        Editor.IsReadOnly = !editable;
        CompleteButton.IsEnabled = canComplete;
        ClearButton.IsEnabled = !busy;
        RefreshDraftState();
    }

    private void RefreshDraftState()
    {
        CharacterCount.Text = $"{StringInfo.ParseCombiningCharacters(Editor.Text).Length} 字";
        CopyButton.IsEnabled = !busy && Editor.Text.Length > 0;
    }

    internal void UseFamily(GamepadFamily family) => glyph.Family = family;
    private void Complete(object sender, RoutedEventArgs e) => CompleteRequested?.Invoke();
    private void Cancel(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
    private void Copy(object sender, RoutedEventArgs e) => CopyRequested?.Invoke();
    private void Clear(object sender, RoutedEventArgs e) { ClearRequested?.Invoke(); Editor.Focus(); }

    internal void PlaceAbove(Rectangle overlay)
    {
        var screen = System.Windows.Forms.Screen.FromRectangle(overlay);
        var area = screen.WorkingArea;
        var handle = Handle;
        // Position in physical pixels, just like the WinForms grid. Move to the
        // destination monitor before querying DPI, without activating the window.
        if (System.Windows.Forms.Screen.FromHandle(handle).DeviceName != screen.DeviceName)
            SetWindowPos(handle, 0, overlay.Left, area.Top, 0, 0, 0x0015); // NOSIZE | NOZORDER | NOACTIVATE
        var scale = GetDpiForWindow(handle) / 96d;
        if (scale <= 0) scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var width = Math.Min(Math.Max(overlay.Width, (int)Math.Ceiling(620 * scale)), area.Width - 32);
        var height = Math.Min((int)Math.Ceiling(DesignHeight * scale), area.Height);
        SetWindowPos(handle, new nint(-1), Math.Clamp(overlay.Right - width, area.Left, area.Right - width),
            Math.Max(area.Top, overlay.Top - height - 8), width, height, 0x0010); // TOPMOST | NOACTIVATE
    }

    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
