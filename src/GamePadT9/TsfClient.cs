using System.Runtime.InteropServices;
using System.Text;

namespace GamePadT9;

internal enum InputBackend { Xiaobai, Standalone }
internal readonly record struct Target(nint Endpoint, nint Foreground, uint Epoch, uint Process, InputBackend Backend);
internal sealed class TsfClient
{
    private const uint ProbeMessage = 0x8000 + 91, ReceiptMessage = 0x8000 + 92;
    private uint request = (uint)Random.Shared.Next(1, int.MaxValue);
    public bool LastOutcomeUncertain { get; private set; }
    public static bool SupportsBackspace(Target target) => Send(target.Endpoint, 0x8000 + 94, 0, 0, out var flags) && (flags & 2) != 0;
    public static bool AllowNumeric(Target target)
    {
        if (FindTarget() != target) return false;
        // The standalone TIP has no key sink, so NumPad events reach the editor directly.
        if (target.Backend == InputBackend.Standalone) return true;
        return Send(target.Endpoint, 0x8000 + 95, unchecked((nint)target.Epoch), 0, out var result) && result == 1;
    }
    public static Target? FindTarget()
    {
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var pid);
        foreach (var backend in new[] { InputBackend.Xiaobai, InputBackend.Standalone })
        {
            var className = backend == InputBackend.Xiaobai ? "GamePadT9.Xiaobai.v1" : "GamePadT9.TextService.v1";
            nint endpoint = 0;
            while ((endpoint = FindWindowEx(new nint(-3), endpoint, className, null)) != 0)
            {
                GetWindowThreadProcessId(endpoint, out var endpointPid);
                if (endpointPid != pid) continue;
                if (Send(endpoint, ProbeMessage, 0, 0, out var token) && token is > 0 and <= int.MaxValue)
                    return new(endpoint, foreground, (uint)token, pid, backend);
            }
        }
        return null;
    }
    public Task<string?> Commit(Target target, string text) => Edit(target, 1, text);
    public Task<string?> Backspace(Target target)
    {
        LastOutcomeUncertain = false;
        if (!SupportsBackspace(target))
            return Task.FromResult<string?>("当前程序仍加载旧输入组件，请重新打开该程序后使用上屏退格");
        return Edit(target, 2, "");
    }
    private async Task<string?> Edit(Target target, uint operation, string text)
    {
        LastOutcomeUncertain = false;
        if (FindTarget() != target) return "目标文本框已经变化，未提交。";
        if (operation == 1 && text.Length is 0 or > 1024) return "提交文本长度无效。";
        var id = ++request; if (id == 0) id = ++request;
        var bytes = new byte[16 + text.Length * 2];
        BitConverter.GetBytes(operation).CopyTo(bytes, 0);
        BitConverter.GetBytes(target.Epoch).CopyTo(bytes, 4);
        BitConverter.GetBytes(id).CopyTo(bytes, 8);
        BitConverter.GetBytes((uint)text.Length).CopyTo(bytes, 12);
        Encoding.Unicode.GetBytes(text).CopyTo(bytes, 16);
        var data = Marshal.AllocHGlobal(bytes.Length);
        var packetPtr = Marshal.AllocHGlobal(Marshal.SizeOf<CopyData>());
        bool accepted;
        try
        {
            Marshal.Copy(bytes, 0, data, bytes.Length);
            Marshal.StructureToPtr(new CopyData { Magic = 0x39545047, Size = (uint)bytes.Length, Data = data }, packetPtr, false);
            accepted = Send(target.Endpoint, 0x004A, target.Foreground, packetPtr, out var result) && result == 1;
        }
        finally { Marshal.FreeHGlobal(packetPtr); Marshal.FreeHGlobal(data); }
        // Even on a message timeout, query the receipt: the receiver may already have inserted.
        for (var i = 0; i < 100; i++)
        {
            if (Send(target.Endpoint, ReceiptMessage, unchecked((nint)id), 0, out var status))
            {
                if (status == 2) return null;
                if (status == 3)
                {
                    if (Send(target.Endpoint, 0x8000 + 93, 0, 0, out var hr) && (uint)hr == 0x80004001)
                        return "目标控件未开放完整文档，无法直接删除已上屏文字";
                    return "TSF 编辑失败，文本未提交。";
                }
                if (status == 0 && !accepted) return "TSF 拒绝请求，请检查当前文本框及输入法。";
            }
            await Task.Delay(20);
        }
        LastOutcomeUncertain = true;
        return "未收到 TSF 完成确认。为避免重复文字，不自动重试；请检查目标文本框后按 B 清除。";
    }
    private static bool Send(nint hwnd, uint message, nint wp, nint lp, out nuint result)
        => SendMessageTimeout(hwnd, message, wp, lp, 0x0002 | 0x0020, 250, out result) != 0;
    [StructLayout(LayoutKind.Sequential)] private struct CopyData { public nuint Magic; public uint Size; public nint Data; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")] private static extern nint FindWindowEx(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wp, nint lp, uint flags, uint timeout, out nuint result);
}
