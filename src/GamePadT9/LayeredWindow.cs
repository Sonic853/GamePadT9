using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GamePadT9;

// Per-pixel alpha is required: Form.Opacity would multiply the opacity of every region.
internal static class LayeredWindow
{
    internal static void Update(nint window, Bitmap bitmap, Point location)
    {
        var info = new BitmapInfo { Size = 40, Width = bitmap.Width, Height = -bitmap.Height, Planes = 1, Bits = 32 };
        var dc = CreateCompatibleDC(0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        nint dib = 0, previous = 0;
        try
        {
            dib = CreateDIBSection(dc, ref info, 0, out var pixels, 0, 0);
            if (dib == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var data = bitmap.LockBits(new(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new byte[bitmap.Width * 4];
                for (var y = 0; y < bitmap.Height; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                    Marshal.Copy(row, 0, pixels + y * row.Length, row.Length);
                }
            }
            finally { bitmap.UnlockBits(data); }
            previous = SelectObject(dc, dib);
            var size = bitmap.Size; var origin = Point.Empty;
            var blend = new BlendFunction { ConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(window, 0, ref location, ref size, dc, ref origin, 0, ref blend, 2))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            if (previous != 0) SelectObject(dc, previous);
            if (dib != 0) DeleteObject(dib);
            DeleteDC(dc);
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BlendFunction { public byte Operation, Flags, ConstantAlpha, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XPixels, YPixels;
        public uint Colors, ImportantColors;
    }
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UpdateLayeredWindow(nint window, nint screenDc, ref Point location, ref Size size, nint sourceDc, ref Point source, uint key, ref BlendFunction blend, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint pixels, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteDC(nint dc);
}
