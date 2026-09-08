using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GamePadT9;

// The user's explicit compatibility exception: only a bare Backspace key pair.
internal static class BackspaceInput
{
    internal static uint SentKeyEvents { get; private set; }
    internal static void Send(Target target)
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0)
                throw new InvalidOperationException("请松开键盘修饰键后退格。");
        if (TsfClient.FindTarget() != target)
            throw new InvalidOperationException("输入焦点已变化，退格未发送。");
        Input[] keys = [new() { Type = 1, Union = new() { Key = new() { Vk = 0x08, Extra = 0x47543942 } } },
                        new() { Type = 1, Union = new() { Key = new() { Vk = 0x08, Flags = 2, Extra = 0x47543942 } } }];
        var sent = SendInput((uint)keys.Length, keys, Marshal.SizeOf<Input>());
        SentKeyEvents += sent;
        if (sent != keys.Length)
        {
            var error = Marshal.GetLastWin32Error();
            if (sent == 1) SentKeyEvents += SendInput(1, [keys[1]], Marshal.SizeOf<Input>());
            throw new Win32Exception(error, "系统未完整接收 Backspace，请检查目标程序权限和删除结果。");
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
