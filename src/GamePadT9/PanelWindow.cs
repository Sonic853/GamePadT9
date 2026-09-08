using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace GamePadT9;

// WPF configuration windows share the tray's STA and WinForms message loop.
// Explicit shutdown keeps closing a configuration window from stopping input.
public class PanelWindow : FluentWindow, IDisposable
{
    private bool closed;
    internal nint Handle => new WindowInteropHelper(this).EnsureHandle();
    public PanelWindow()
    {
        EnsureResources();
        // FluentWindow sets a dynamic style reference in its base constructor, before
        // a WinForms host has created WPF application resources. Resolve it now.
        Style = (Style)FindResource(typeof(FluentWindow));
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI, Microsoft YaHei UI");
        FontSize = 14;
        Background = (System.Windows.Media.Brush)FindResource("ApplicationBackgroundBrush");
        Foreground = (System.Windows.Media.Brush)FindResource("TextFillColorPrimaryBrush");
        WindowBackdropType = WindowBackdropType.Mica;
        ExtendsContentIntoTitleBar = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(this);
        SourceInitialized += (_, _) =>
        {
            var area = System.Windows.Forms.Screen.FromHandle(Handle).WorkingArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            var availableWidth = Math.Max(480, area.Width / dpi.DpiScaleX - 32);
            var availableHeight = Math.Max(400, area.Height / dpi.DpiScaleY - 32);
            MinWidth = Math.Min(MinWidth, availableWidth); MinHeight = Math.Min(MinHeight, availableHeight);
            Width = Math.Min(Width, availableWidth); Height = Math.Min(Height, availableHeight);
        };
        Loaded += (_, _) =>
        {
            // A queued load/render callback may arrive after a quick save or cancel.
            // Never recreate the HWND of a window that has already closed.
            var handle = new WindowInteropHelper(this).Handle;
            if (!closed && IsVisible && handle != 0) ShowWindow(handle, 1);
        };
        Closed += (_, _) => closed = true;
    }
    private static void EnsureResources()
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        if (app.Resources.Contains("GamePadPanelResources")) return;
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/GamePadT9;component/PanelResources.xaml", UriKind.Relative) });
        app.Resources["GamePadPanelResources"] = true;
    }
    internal T Control<T>(string name) where T : FrameworkElement => (T)(FindName(name) ?? throw new InvalidOperationException("找不到控件：" + name));
    internal void Click(string name) => Control<System.Windows.Controls.Primitives.ButtonBase>(name).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
    public void Dispose() { if (!closed) Close(); GC.SuppressFinalize(this); }
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
}
