using System.Runtime.InteropServices;

namespace GamePadT9;

// Verification captures the actual composited window, including its native title bar.
internal static class PanelSnapshot
{
    internal static void Save(PanelWindow window, string file)
    {
        if (!GetWindowRect(window.Handle, out var bounds)) throw new InvalidOperationException("无法读取面板位置。");
        using var bitmap = new Bitmap(bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
        using (var g = Graphics.FromImage(bitmap)) g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size);
        bitmap.Save(file);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
}
