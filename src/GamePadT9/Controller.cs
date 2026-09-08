using System.Runtime.InteropServices;

namespace GamePadT9;

[Flags] internal enum Buttons : ushort
{
    Left = 4, Right = 8, Menu = 16, View = 32, L3 = 64, R3 = 128,
    LB = 256, RB = 512, A = 4096, B = 8192, X = 16384, Y = 32768
}
[StructLayout(LayoutKind.Sequential)] internal struct Gamepad
{
    public Buttons Buttons;
    public byte LT, RT;
    public short LX, LY, RX, RY;
}
[StructLayout(LayoutKind.Sequential)] internal struct PadState { public uint Packet; public Gamepad Pad; }
internal enum PadAction { Toggle, SwitchMode, Region, Confirm, Cancel, Disable, Backspace, Previous, Next, PagePrevious, PageNext }
internal readonly record struct PadEvent(PadAction Action, int Region = 4, bool StickClick = false);

internal sealed class Controller
{
    private ControlSide stick = ControlSide.Right, trigger = ControlSide.Right;
    private Gamepad previous;
    private bool initialized, triggerHeld, chordHeld, longB;
    private long bSince;
    private int column = 1, row = 1;
    private Buttons repeating;
    private long nextRepeat;
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
    public void Reset() { initialized = triggerHeld = chordHeld = longB = false; row = column = 1; repeating = 0; }
    public void Configure(UserSettings settings)
    {
        settings.Validate();
        if (stick == settings.Stick && trigger == settings.Trigger) return;
        stick = settings.Stick; trigger = settings.Trigger; Reset();
    }
    public List<PadEvent> Update(Gamepad pad, long now)
    {
        var events = new List<PadEvent>();
        var stableRegion = Region;
        column = Axis(stick == ControlSide.Left ? pad.LX : pad.RX, column);
        row = Axis(-(int)(stick == ControlSide.Left ? pad.LY : pad.RY), row);
        var triggerValue = trigger == ControlSide.Left ? pad.LT : pad.RT;
        var stickButton = stick == ControlSide.Left ? Buttons.L3 : Buttons.R3;
        var chord = (pad.Buttons & (Buttons.Menu | Buttons.View)) == (Buttons.Menu | Buttons.View);
        if (!initialized)
        {
            initialized = true; previous = pad; triggerHeld = triggerValue >= 100; chordHeld = chord;
            bSince = now; longB = (pad.Buttons & Buttons.B) != 0;
            return events;
        }
        bool Down(Buttons b) => (pad.Buttons & b) != 0 && (previous.Buttons & b) == 0;
        bool PressOrRepeat(Buttons b)
        {
            if (Down(b)) { repeating = b; nextRepeat = now + 420; return true; }
            if (repeating == b && (pad.Buttons & b) != 0 && now >= nextRepeat)
            { nextRepeat = now + 110; return true; }
            return false;
        }
        if ((pad.Buttons & repeating) == 0) repeating = 0;
        var triggerDown = !triggerHeld && triggerValue >= 160;
        if (triggerValue >= 160) triggerHeld = true;
        else if (triggerValue <= 100) triggerHeld = false;
        if (chord && !chordHeld) events.Add(new(PadAction.Toggle));
        if (Down(Buttons.B)) { bSince = now; longB = false; }
        if ((pad.Buttons & Buttons.B) != 0 && !longB && now - bSince >= 1000)
        { longB = true; events.Add(new(PadAction.Disable)); }
        if ((pad.Buttons & Buttons.B) == 0 && (previous.Buttons & Buttons.B) != 0 && !longB) events.Add(new(PadAction.Cancel));
        // Mode and cancellation events take precedence over typing on the same sample.
        if (!chord && (pad.Buttons & Buttons.B) == 0 && events.Count == 0)
        {
            if (Down(Buttons.Y)) events.Add(new(PadAction.SwitchMode));
            else if (Down(Buttons.A)) events.Add(new(PadAction.Confirm));
            else if (PressOrRepeat(Buttons.X)) events.Add(new(PadAction.Backspace));
            else if (PressOrRepeat(Buttons.LB)) events.Add(new(PadAction.Previous));
            else if (PressOrRepeat(Buttons.RB)) events.Add(new(PadAction.Next));
            else if (Down(Buttons.Left)) events.Add(new(PadAction.PagePrevious));
            else if (Down(Buttons.Right)) events.Add(new(PadAction.PageNext));
            else if (triggerDown || Down(stickButton)) events.Add(new(PadAction.Region, Down(stickButton) ? stableRegion : Region, Down(stickButton)));
        }
        previous = pad; chordHeld = chord; return events;
    }
}
