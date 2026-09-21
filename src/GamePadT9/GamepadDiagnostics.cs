using System.Text.Json;

namespace GamePadT9;

// Hardware-only sampling. Never opens an input session or forwards button events.
internal static class GamepadDiagnostics
{
    private sealed class Observation(GamepadDevice device)
    {
        public string Name { get; } = device.Name;
        public GamepadFamily Family { get; } = device.Family;
        public string Vendor { get; } = device.Vendor.ToString("x4");
        public string Product { get; } = device.Product.ToString("x4");
        public int Touchpads { get; } = device.Touchpads;
        public HashSet<int> LeftRegions { get; } = [];
        public HashSet<int> RightRegions { get; } = [];
        public bool LeftClick { get; private set; }
        public bool RightClick { get; private set; }
        public bool LeftReleased { get; private set; }
        public bool RightReleased { get; private set; }
        public HashSet<string> ButtonsSeen { get; } = [];
        internal void Sample(Gamepad state)
        {
            static int Region(short x, short y) => (y > 12500 ? 0 : y < -12500 ? 2 : 1) * 3 + (x < -12500 ? 0 : x > 12500 ? 2 : 1) + 1;
            if (state.LeftTouch) LeftRegions.Add(Region(state.LX, state.LY)); else if (LeftRegions.Count > 0) LeftReleased = true;
            if (state.RightTouch) RightRegions.Add(Region(state.RX, state.RY)); else if (RightRegions.Count > 0) RightReleased = true;
            LeftClick |= (state.TouchpadButtons & Buttons.L3) != 0;
            RightClick |= (state.TouchpadButtons & Buttons.R3) != 0;
            if (state.Buttons != 0) ButtonsSeen.Add(state.Buttons.ToString());
            if (state.LT > 160) ButtonsSeen.Add("LT");
            if (state.RT > 160) ButtonsSeen.Add("RT");
        }
    }
    internal static int Run(int seconds)
    {
        if (seconds is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(seconds), "采样时长为 1–60 秒。");
        using var devices = new GamepadDevices();
        var observations = new Dictionary<uint, Observation>();
        var end = Environment.TickCount64 + seconds * 1000;
        do
        {
            devices.Poll(null, out _);
            foreach (var device in devices.Connected.ToArray())
            {
                if (!observations.TryGetValue(device.Instance, out var observation)) observations.Add(device.Instance, observation = new(device));
                if (devices.Poll(device.Id, out var state)) observation.Sample(state);
            }
            Thread.Sleep(10);
        } while (Environment.TickCount64 < end);
        Console.WriteLine(JsonSerializer.Serialize(new { seconds, devices = observations.Values, error = devices.Error }, new JsonSerializerOptions { WriteIndented = true }));
        return devices.Error == null ? 0 : 1;
    }
}
