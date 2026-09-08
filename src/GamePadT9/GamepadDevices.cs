using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace GamePadT9;

[JsonConverter(typeof(JsonStringEnumConverter<GamepadFamily>))]
internal enum GamepadFamily { Xbox, PS4, PS5 }
internal sealed record GamepadDevice(uint Instance, string Id, string Name, GamepadFamily Family);

// One SDL owner on the WinForms thread. No SDL window or Steam process is required.
internal sealed class GamepadDevices : IDisposable
{
    private readonly Dictionary<uint, (nint Handle, GamepadDevice Device)> opened = [];
    private long nextScan;
    private bool disposed;
    internal IReadOnlyList<GamepadDevice> Connected { get; private set; } = [];
    internal GamepadDevice? Active { get; private set; }
    internal string? Error { get; private set; }

    internal GamepadDevices()
    {
        Sdl.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
        Sdl.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS4", "1");
        Sdl.SDL_SetHint("SDL_JOYSTICK_HIDAPI_PS5", "1");
        if (!Sdl.SDL_InitSubSystem(Sdl.GamepadSubsystem))
            throw new InvalidOperationException("无法初始化手柄：" + Sdl.Error);
        Sdl.SDL_SetGamepadEventsEnabled(false);
        Sdl.SDL_SetJoystickEventsEnabled(false);
    }

    internal bool Poll(string? preferred, out Gamepad state, bool rescan = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        state = default;
        Sdl.SDL_UpdateGamepads();
        if (rescan || Environment.TickCount64 >= nextScan ||
            (Active != null && !Sdl.SDL_GamepadConnected(opened[Active.Instance].Handle)))
        {
            Scan(); nextScan = Environment.TickCount64 + 750;
        }
        Active = Select(Connected, preferred, Active);
        if (Active == null) return false;
        var handle = opened[Active.Instance].Handle;
        if (!Sdl.SDL_GamepadConnected(handle)) { Active = null; return false; }
        state = Read(handle);
        return true;
    }

    internal static GamepadDevice? Select(IReadOnlyList<GamepadDevice> devices, string? preferred, GamepadDevice? current)
    {
        if (preferred != null) return devices.FirstOrDefault(d => d.Id == preferred);
        return devices.FirstOrDefault(d => d.Instance == current?.Instance) ?? devices.FirstOrDefault();
    }

    private void Scan()
    {
        var memory = Sdl.SDL_GetGamepads(out var count);
        if (memory == 0) { Error = Sdl.Error; return; }
        var ids = new HashSet<uint>();
        Error = null;
        try
        {
            for (var i = 0; i < count; i++) ids.Add(unchecked((uint)Marshal.ReadInt32(memory, i * 4)));
            foreach (var id in opened.Keys.Where(id => !ids.Contains(id)).ToArray())
            { Sdl.SDL_CloseGamepad(opened[id].Handle); opened.Remove(id); }
            foreach (var id in ids.Order())
            {
                if (opened.ContainsKey(id)) continue;
                var handle = Sdl.SDL_OpenGamepad(id);
                if (handle == 0) { Error = Sdl.Error; continue; }
                try
                {
                    var type = Sdl.SDL_GetRealGamepadType(handle);
                    var family = type switch { 5 => GamepadFamily.PS4, 6 => GamepadFamily.PS5, _ => GamepadFamily.Xbox };
                    var name = Sdl.String(Sdl.SDL_GetGamepadName(handle));
                    if (name.Length == 0) name = family.ToString() + " 手柄";
                    var vendor = Sdl.SDL_GetGamepadVendor(handle); var product = Sdl.SDL_GetGamepadProduct(handle);
                    var serial = Sdl.String(Sdl.SDL_GetGamepadSerial(handle));
                    var path = Sdl.String(Sdl.SDL_GetGamepadPath(handle));
                    // Serial survives USB/Bluetooth changes when provided. Otherwise retain the device path / XInput slot.
                    var identity = serial.Length > 0 ? $"serial:{vendor:x4}:{product:x4}:{serial}" :
                        path.Length > 0 ? "path:" + path : $"slot:{vendor:x4}:{product:x4}:{name}:{Sdl.SDL_GetGamepadPlayerIndex(handle)}";
                    var stableId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                    // Identical devices without a path/serial must still be individually selectable this session.
                    if (opened.Values.Any(x => x.Device.Id == stableId)) stableId += ":" + id;
                    opened.Add(id, (handle, new(id, stableId, name, family)));
                }
                catch { Sdl.SDL_CloseGamepad(handle); throw; }
            }
            Connected = opened.Values.Select(x => x.Device).OrderBy(x => x.Instance).ToArray();
        }
        finally { Sdl.SDL_free(memory); }
    }

    private static readonly Buttons[] ButtonMap =
        [Buttons.A, Buttons.B, Buttons.X, Buttons.Y, Buttons.View, 0, Buttons.Menu, Buttons.L3, Buttons.R3, Buttons.LB, Buttons.RB, 0, 0, Buttons.Left, Buttons.Right];
    private static Gamepad Read(nint handle)
    {
        Buttons buttons = 0;
        for (var i = 0; i < ButtonMap.Length; i++) if (Sdl.SDL_GetGamepadButton(handle, i)) buttons |= ButtonMap[i];
        return new()
        {
            Buttons = buttons, LX = Sdl.SDL_GetGamepadAxis(handle, 0), LY = InvertY(Sdl.SDL_GetGamepadAxis(handle, 1)),
            RX = Sdl.SDL_GetGamepadAxis(handle, 2), RY = InvertY(Sdl.SDL_GetGamepadAxis(handle, 3)),
            LT = Trigger(Sdl.SDL_GetGamepadAxis(handle, 4)), RT = Trigger(Sdl.SDL_GetGamepadAxis(handle, 5))
        };
    }
    internal static short InvertY(short value) => (short)Math.Clamp(-(int)value, short.MinValue, short.MaxValue);
    internal static byte Trigger(short value) => (byte)((Math.Max(0, (int)value) * 255 + 16383) / 32767);
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var item in opened.Values) Sdl.SDL_CloseGamepad(item.Handle);
        opened.Clear(); Connected = []; Active = null;
        Sdl.SDL_QuitSubSystem(Sdl.GamepadSubsystem);
    }
}

// SDL3's bool is one byte; all exports use cdecl, including in the x86 host.
internal static class Sdl
{
    internal const uint GamepadSubsystem = 0x2000;
    internal static string String(nint value) => Marshal.PtrToStringUTF8(value) ?? "";
    internal static string Error => String(SDL_GetError());
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] private static extern nint SDL_GetError();
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_InitSubSystem(uint flags);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_QuitSubSystem(uint flags);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_SetGamepadEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_SetJoystickEventsEnabled([MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_UpdateGamepads();
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetGamepads(out int count);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_free(nint pointer);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_OpenGamepad(uint id);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern void SDL_CloseGamepad(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GamepadConnected(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetGamepadName(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetGamepadPath(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern nint SDL_GetGamepadSerial(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetGamepadVendor(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern ushort SDL_GetGamepadProduct(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetGamepadPlayerIndex(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern int SDL_GetRealGamepadType(nint gamepad);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] internal static extern bool SDL_GetGamepadButton(nint gamepad, int button);
    [DllImport("SDL3", CallingConvention = CallingConvention.Cdecl)] internal static extern short SDL_GetGamepadAxis(nint gamepad, int axis);
}
