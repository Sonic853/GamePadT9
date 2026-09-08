using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GamePadT9;

internal enum InputMode { T9, Numeric }

// The application's only keyboard-injection boundary. It permits NumPad 0..9
// exclusively, and requires both Numeric mode and the same foreground window.
internal static class NumericInput
{
    internal static uint SentKeyEvents { get; private set; }
    internal static void SendDigit(InputMode mode, int digit, nint foreground)
    {
        if (mode != InputMode.Numeric) throw new InvalidOperationException("九键模式不能发送模拟按键。");
        if ((uint)digit > 9) throw new ArgumentOutOfRangeException(nameof(digit));
        if (foreground == 0 || TsfClient.GetForegroundWindow() != foreground)
            throw new InvalidOperationException("输入焦点已变化，数字未发送。");
        foreach (var modifier in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(modifier) & 0x8000) != 0)
                throw new InvalidOperationException("请松开键盘修饰键后输入数字。");
        var target = TsfClient.FindTarget();
        if (target == null || target.Value.Foreground != foreground || !TsfClient.AllowNumeric(target.Value))
            throw new InvalidOperationException("请选择已适配的小白 T9 或 GamePad T9，并结束键盘正在输入的拼音。");
        // Virtual key mode: no scancode flag, no NumLock toggling, no Unicode injection.
        var vk = (ushort)(0x60 + digit);
        Input[] keys = [new() { Type = 1, Union = new() { Key = new() { Vk = vk, Extra = 0x4754394e } } },
                        new() { Type = 1, Union = new() { Key = new() { Vk = vk, Flags = 2, Extra = 0x4754394e } } }];
        var sent = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
        SentKeyEvents += sent;
        if (sent != keys.Length)
        {
            var error = Marshal.GetLastWin32Error();
            if (sent == 1) SentKeyEvents += SendInput(1, [keys[1]], Marshal.SizeOf<Input>());
            throw new Win32Exception(error, "系统未完整接收小键盘数字，请检查目标程序权限。");
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public Keyboard Key;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
