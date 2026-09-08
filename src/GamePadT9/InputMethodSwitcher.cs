using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GamePadT9;

internal sealed record InputProfile(uint Type, ushort Language, Guid Clsid, Guid Profile, string Keyboard)
{
    internal bool IsValid => Type == 1 || Type == 2 && Keyboard != "0";
    internal string Name => Clsid == new Guid("595B67E9-48A3-4C82-B7B1-64E4A35C9D92") ? "GamePad T9" :
        Clsid == new Guid("A3F4CDED-B1E9-41EE-9CA6-7B4D0DE6CB0A") ? "小白 T9" : "原输入法";
}
internal sealed record InputMethodResult(bool Ok, string Error, InputProfile Before, InputProfile After);
internal readonly record struct InputWindow(nint Handle, uint Process, uint Thread)
{
    internal bool IsAlive => TsfClient.GetWindowThreadProcessId(Handle, out var process) == Thread && process == Process;
    internal nint Root => GetAncestor(Handle, 2);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
}

// Owns the original profiles for the UI threads touched during one enabled interval.
internal sealed class InputMethodSwitcher(string root, bool simulateMissingStandalone = false)
{
    private sealed record Saved(InputWindow Window, InputProfile Profile);
    private readonly Dictionary<(uint, uint), Saved> saved = [];
    private InputWindow? lastAttempt;
    private InputWindow? lastExternalWindow;
    internal bool HasSavedProfiles => saved.Count != 0;
    internal void ObserveForeground() { if (Foreground() is InputWindow window) lastExternalWindow = window; }
    internal void FocusLastWindow()
    {
        if (lastExternalWindow is InputWindow window && window.IsAlive) SetForegroundWindow(window.Root);
    }
    internal static InputWindow? Foreground()
    {
        var window = TsfClient.GetForegroundWindow();
        if (window == 0) return null;
        var thread = TsfClient.GetWindowThreadProcessId(window, out var process);
        if (thread == 0 || process == Environment.ProcessId) return null;
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return null;
        // A foreground frame can host an editor on another UI thread (Windows 11 Notepad).
        // Do not fall back to the frame when keyboard focus is temporarily unavailable.
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(thread, ref gui) || gui.Focus == 0 || (gui.Flags & 0x1e) != 0) return null;
        var focusThread = TsfClient.GetWindowThreadProcessId(gui.Focus, out var focusProcess);
        var focused = new InputWindow(gui.Focus, focusProcess, focusThread);
        if (focusThread == 0 || focusProcess != process || focused.Root != window ||
            TsfClient.GetForegroundWindow() != window) return null;
        return focused;
    }
    internal bool NeedsForegroundSwitch => Foreground() is InputWindow window && window != lastAttempt;
    internal async Task<string> StartAsync()
    {
        if (saved.Count != 0) await RestoreAsync();
        lastAttempt = null;
        return await FollowAsync() ?? throw new InvalidOperationException("请先点击目标程序的文本框，再开启手柄输入。");
    }
    // External editing takes focus before the first commit. Capture now without activating
    // an IME so a later focus transition cannot replace the user's original profile.
    internal async Task CaptureAsync(InputWindow window)
    {
        if (saved.Count != 0) await RestoreAsync();
        lastAttempt = null;
        if (Foreground() != window) throw new InvalidOperationException("原目标焦点已变化，未记录输入法。");
        var result = await RunAsync(window, "query");
        RequireSuccess(result, "无法记录目标窗口的输入法");
        if (Foreground() != window) throw new InvalidOperationException("记录输入法期间目标焦点已变化。");
        saved.Add((window.Process, window.Thread), new(window, result.Before));
    }
    internal async Task<string?> FollowAsync()
    {
        if (Foreground() is not InputWindow window) return null;
        lastAttempt = window;
        var key = (window.Process, window.Thread);
        if (saved.TryGetValue(key, out var existing))
        {
            saved[key] = existing with { Window = window };
        }
        else
        {
            var query = await RunAsync(window, "query");
            RequireSuccess(query, "无法记录目标窗口的输入法");
            saved[key] = new(window, query.Before); // Save before issuing any command that can mutate.
        }
        var result = await RunAsync(window, simulateMissingStandalone ? "switch-fallback" : "switch");
        // The first switch's actual pre-state is authoritative. Revisits never replace it.
        if (existing == null && result.Before.IsValid) saved[key] = new(window, result.Before);
        RequireSuccess(result, "无法切换输入法（请确认已安装 GamePad T9 或小白 T9）");
        await WaitForTargetAsync(window, result.After);
        return "已切换到 " + result.After.Name;
    }
    private static async Task WaitForTargetAsync(InputWindow window, InputProfile profile)
    {
        var backend = profile.Clsid == new Guid("595B67E9-48A3-4C82-B7B1-64E4A35C9D92") ? InputBackend.Standalone : InputBackend.Xiaobai;
        for (var i = 0; i < 80; i++)
        {
            if (Foreground() != window) throw new InvalidOperationException("切换期间输入焦点已变化，请在目标文本框重新开启输入。");
            if (TsfClient.FindTarget() is Target target && target.Backend == backend && Foreground() == window) return;
            await Task.Delay(25);
        }
        throw new InvalidOperationException("输入法已切换，但目标文本框的手柄输入组件尚未就绪；请确认文本框可编辑，并完成或取消键盘正在输入的拼音。");
    }
    internal async Task RestoreAsync()
    {
        var failures = new List<string>();
        foreach (var (key, item) in saved.ToArray().Reverse())
        {
            try
            {
                // A child editor may have been destroyed while its UI thread is still alive.
                var live = FindRestoreWindow(item.Window);
                if (live == null) { saved.Remove(key); continue; }
                var result = await RunAsync(live.Value, "restore", item.Profile).ConfigureAwait(false);
                RequireSuccess(result, "恢复原输入法失败");
                // The in-hook success response precedes queued activation/focus messages.
                // Read the profile again after those messages have had time to run.
                for (var attempt = 0; ; attempt++)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    var check = await RunAsync(live.Value, "query").ConfigureAwait(false);
                    RequireSuccess(check, "无法核对恢复后的输入法");
                    if (check.After == item.Profile) break;
                    if (attempt == 2) throw new InvalidOperationException("原输入法未保持恢复状态，已保留记录供重试。");
                    RequireSuccess(await RunAsync(live.Value, "restore", item.Profile).ConfigureAwait(false), "恢复原输入法失败");
                }
                saved.Remove(key);
            }
            catch (Exception ex) { failures.Add(ex.Message); }
        }
        lastAttempt = null;
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("；", failures.Distinct()));
    }
    internal async Task<InputMethodResult> RunAsync(InputWindow window, string operation, InputProfile? profile = null, nint focusDestination = 0)
    {
        if (!window.IsAlive) throw new InvalidOperationException("目标窗口已关闭或变化。");
        if (operation is "switch" or "switch-fallback" or "grant-focus" && Foreground() != window)
            throw new InvalidOperationException("目标输入焦点已变化，未切换输入法。");
        using var target = Process.GetProcessById((int)window.Process);
        if (!IsWow64Process2(target.Handle, out var processMachine, out var nativeMachine)) throw new Win32Exception(Marshal.GetLastWin32Error());
        var machine = processMachine == 0 ? nativeMachine : processMachine;
        var architecture = machine switch { 0x014c => "x86", 0x8664 => "x64", _ => throw new InvalidOperationException("当前目标程序架构尚不支持自动切换输入法。") };
        var folder = File.ReadAllText(Path.Combine(root, "artifacts", $"input-method-{architecture}-path.txt")).Trim();
        var info = new ProcessStartInfo(Path.Combine(folder, "InputMethodControl.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var arg in new[] { operation, window.Handle.ToString("X"), window.Process.ToString(), window.Thread.ToString() }) info.ArgumentList.Add(arg);
        if (operation == "grant-focus")
        { info.ArgumentList.Add(focusDestination.ToString("X")); info.ArgumentList.Add(Environment.ProcessId.ToString()); }
        if (profile != null)
            foreach (var arg in new[] { profile.Type.ToString(), profile.Language.ToString(), profile.Clsid.ToString("B"), profile.Profile.ToString("B"), profile.Keyboard }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new IOException("无法启动输入法切换组件。");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
        catch (TimeoutException)
        {
            if (!process.HasExited) process.Kill();
            throw new IOException("输入法切换超时，已保留原输入法记录供恢复。");
        }
        var result = JsonSerializer.Deserialize<InputMethodResult>(await output.ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return result ?? throw new IOException("输入法切换组件返回无效结果：" + await error.ConfigureAwait(false));
    }
    private static void RequireSuccess(InputMethodResult result, string description)
    {
        if (!result.Ok) throw new InvalidOperationException(description + "：" + result.Error);
    }
    private static InputWindow? FindRestoreWindow(InputWindow original)
    {
        if (original.IsAlive) return original;
        InputWindow? found = null;
        EnumThreadWindows(original.Thread, (handle, _) =>
        {
            var candidate = original with { Handle = handle };
            if (!candidate.IsAlive) return true;
            found = candidate; return false;
        }, 0);
        if (found != null) return found;
        try
        {
            using var process = Process.GetProcessById((int)original.Process);
            if (process.Threads.Cast<ProcessThread>().Any(t => t.Id == original.Thread))
                throw new InvalidOperationException("原编辑控件已关闭，但输入线程仍存在且没有可用恢复窗口；已保留原输入法记录。");
        }
        catch (ArgumentException) { } // The target process has exited.
        return null;
    }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    private delegate bool EnumWindow(nint window, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, nint parameter);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(nint process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
