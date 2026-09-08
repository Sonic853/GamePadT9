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
        if (lastExternalWindow is InputWindow window && window.IsAlive) SetForegroundWindow(window.Handle);
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
        return new(window, process, thread);
    }
    internal bool NeedsForegroundSwitch => Foreground() is InputWindow window && window != lastAttempt;
    internal async Task<string> StartAsync()
    {
        if (saved.Count != 0) await RestoreAsync();
        lastAttempt = null;
        return await FollowAsync() ?? throw new InvalidOperationException("请先点击目标程序的文本框，再开启手柄输入。");
    }
    internal async Task<string?> FollowAsync()
    {
        if (Foreground() is not InputWindow window) return null;
        lastAttempt = window;
        var key = (window.Process, window.Thread);
        if (saved.TryGetValue(key, out var existing))
        {
            saved[key] = existing with { Window = window };
            return null; // Do not overwrite the original profile when revisiting a thread.
        }
        var query = await RunAsync(window, "query");
        RequireSuccess(query, "无法记录目标窗口的输入法");
        saved[key] = new(window, query.Before); // Save before issuing any command that can mutate.
        var result = await RunAsync(window, simulateMissingStandalone ? "switch-fallback" : "switch");
        if (result.Before.IsValid) saved[key] = new(window, result.Before);
        RequireSuccess(result, "无法切换输入法（请确认已安装 GamePad T9 或小白 T9）");
        return "已切换到 " + result.After.Name;
    }
    internal async Task RestoreAsync()
    {
        var failures = new List<string>();
        foreach (var (key, item) in saved.ToArray().Reverse())
        {
            if (!item.Window.IsAlive) { saved.Remove(key); continue; }
            try
            {
                var result = await RunAsync(item.Window, "restore", item.Profile).ConfigureAwait(false);
                RequireSuccess(result, "恢复原输入法失败");
                saved.Remove(key);
            }
            catch (Exception ex) { failures.Add(ex.Message); }
        }
        lastAttempt = null;
        if (failures.Count != 0) throw new InvalidOperationException(string.Join("；", failures.Distinct()));
    }
    internal async Task<InputMethodResult> RunAsync(InputWindow window, string operation, InputProfile? profile = null)
    {
        if (!window.IsAlive) throw new InvalidOperationException("目标窗口已关闭或变化。");
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
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process2(nint process, out ushort processMachine, out ushort nativeMachine);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
}
