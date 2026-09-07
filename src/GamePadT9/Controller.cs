using System.Runtime.InteropServices;

namespace GamePadT9;

[Flags] internal enum Buttons : ushort
{
    Left = 4, Right = 8, Menu = 16, View = 32, R3 = 128,
    LB = 256, RB = 512, A = 4096, B = 8192, X = 16384, Y = 32768
}
[StructLayout(LayoutKind.Sequential)] internal struct Gamepad
{
    public Buttons Buttons;
    public byte LT, RT;
    public short LX, LY, RX, RY;
}
[StructLayout(LayoutKind.Sequential)] internal struct PadState { public uint Packet; public Gamepad Pad; }
internal enum PadAction { Toggle, Region, Confirm, Cancel, Disable, Backspace, Previous, Next, PagePrevious, PageNext }
internal readonly record struct PadEvent(PadAction Action, int Region = 4);

internal sealed class Controller
{
    private Gamepad previous;
    private bool initialized, triggerHeld, chordHeld, longB;
    private long bSince;
    private int column = 1, row = 1;
    public int Region => row * 3 + column;
    public static bool Read(uint index, out PadState state) => XInputGetState(index, out state) == 0;
    [DllImport("xinput1_4.dll", ExactSpelling = true)] private static extern uint XInputGetState(uint index, out PadState state);
    private static int Axis(int value, int old)
    {
        const int enter = 12500, leave = 9000;
        if (value > enter) return 2;
        if (value < -enter) return 0;
        if (old == 2 && value > leave) return 2;
        if (old == 0 && value < -leave) return 0;
        return 1;
    }
    public void Reset() { initialized = triggerHeld = chordHeld = longB = false; row = column = 1; }
    public List<PadEvent> Update(Gamepad pad, long now)
    {
        var events = new List<PadEvent>();
        var stableRegion = Region;
        column = Axis(pad.RX, column); row = Axis(-(int)pad.RY, row);
        var chord = (pad.Buttons & (Buttons.Menu | Buttons.View)) == (Buttons.Menu | Buttons.View);
        if (!initialized)
        {
            initialized = true; previous = pad; triggerHeld = pad.RT >= 100; chordHeld = chord;
            bSince = now; longB = (pad.Buttons & Buttons.B) != 0;
            return events;
        }
        bool Down(Buttons b) => (pad.Buttons & b) != 0 && (previous.Buttons & b) == 0;
        var rtDown = !triggerHeld && pad.RT >= 160;
        if (pad.RT >= 160) triggerHeld = true;
        else if (pad.RT <= 100) triggerHeld = false;
        if (chord && !chordHeld) events.Add(new(PadAction.Toggle));
        if (Down(Buttons.B)) { bSince = now; longB = false; }
        if ((pad.Buttons & Buttons.B) != 0 && !longB && now - bSince >= 1000)
        { longB = true; events.Add(new(PadAction.Disable)); }
        if ((pad.Buttons & Buttons.B) == 0 && (previous.Buttons & Buttons.B) != 0 && !longB) events.Add(new(PadAction.Cancel));
        // Mode and cancellation events take precedence over typing on the same sample.
        if (!chord && events.Count == 0)
        {
            if (Down(Buttons.A)) events.Add(new(PadAction.Confirm));
            else if (Down(Buttons.X)) events.Add(new(PadAction.Backspace));
            else if (Down(Buttons.LB)) events.Add(new(PadAction.Previous));
            else if (Down(Buttons.RB)) events.Add(new(PadAction.Next));
            else if (Down(Buttons.Left)) events.Add(new(PadAction.PagePrevious));
            else if (Down(Buttons.Right)) events.Add(new(PadAction.PageNext));
            else if (rtDown || Down(Buttons.R3)) events.Add(new(PadAction.Region, Down(Buttons.R3) ? stableRegion : Region));
        }
        previous = pad; chordHeld = chord; return events;
    }
}
