namespace GamePadT9;

internal static class EnglishValidation
{
    internal static void Run(Action<bool, string> check)
    {
        var selection = new EnglishSelection();
        var pad = new Controller(selection); pad.ConfigureEnglish(true); pad.Update(default, 0);
        long time = 10;
        PadEvent Press(Gamepad state, bool click = false)
        {
            pad.Update(state, time++);
            if (click) state.Buttons = Buttons.R3; else state.RT = 200;
            var action = pad.Update(state, time++).Single();
            check(pad.Update(state, time++).Count == 0, "English confirmation never repeats while held");
            state.Buttons = 0; state.RT = 0; pad.Update(state, time++);
            return action;
        }
        var opened = Press(new() { RY = 22000 });
        check(selection.Consume(opened) && selection.Group == 1, "Right-only trigger opens ABC without typing a group");
        pad.Update(default, time++);
        check(pad.Region == 1 && pad.Detail == -1, "Centering retains four cells with no selected letter");
        check(selection.Consume(Press(default, true)) && selection.Group == null, "Centered stick click returns to nine groups");
        check(selection.Consume(Press(new() { RY = 22000 }, true)), "Right-only stick click also opens the selected group");
        var letter = Press(new() { RX = 22000, RY = -22000 }, true);
        check(letter.Region == 1 && letter.Detail == 1 && T9Layout.EnglishText(letter) == "b" && !selection.Consume(letter),
            "A stick click inside the locked group types the right-stick letter");
        letter = Press(new() { RX = 22000, RY = -22000, LX = -22000, LY = -22000 });
        check(T9Layout.EnglishText(letter) == "c", "The detail stick takes priority over the group stick inside four cells");
        letter = Press(new() { RX = 22000, RY = 22000, LX = -22000, LY = 22000 });
        check(letter.Detail == 3 && T9Layout.EnglishText(letter) == "", "A blank detail remains selected and blocks the lower-priority valid letter");
        pad.Update(default, time++); pad.Update(new() { Buttons = Buttons.B }, time++);
        check(selection.Consume(pad.Update(default, time++).Single()) && selection.Group == null,
            "Short B returns from four cells without cancelling the composition");
        var symbol = Press(new() { RX = -22000, RY = 22000 }, true);
        check(symbol.Region == 0 && !selection.Consume(symbol) && selection.Group == null,
            "The symbol group bypasses the letter step for trigger or stick click");
        for (var group = 1; group < 9; group++)
        for (var detail = 0; detail < 4; detail++)
        {
            var value = T9Layout.EnglishText(new(PadAction.Region, group, StickClick: true, Detail: detail));
            check(value == (detail < T9Layout.Groups[group].Length ? T9Layout.Groups[group][detail].ToString().ToLowerInvariant() : ""),
                $"English literal quadrant {group}/{detail} has no group fallback or prediction");
            check(T9Layout.EnglishText(new(PadAction.Region, group, Detail: detail), uppercase: true) ==
                (detail < T9Layout.Groups[group].Length ? T9Layout.Groups[group][detail].ToString() : ""),
                $"Uppercase quadrant {group}/{detail} preserves letters and blank cells");
        }
        pad.Configure(new() { Stick = ControlSide.Left, Trigger = ControlSide.Left }); pad.Update(default, time++);
        var swapped = pad.Update(new() { LY = 22000, RX = 22000, RY = -22000, LT = 200 }, time++).Single();
        check(swapped.Region == 1 && T9Layout.EnglishText(swapped) == "b", "Swapped stick and trigger settings preserve English detail priority");
        selection.Consume(new(PadAction.Region, 1)); pad.Reset();
        check(selection.Group == null && pad.Detail == -1, "Disconnect/reset clears the latched group and both stick selections");
        selection.Consume(new(PadAction.Region, 6)); pad.ConfigureEnglish(false);
        check(selection.Group == null, "Leaving English or disabling input clears the four-cell state");
        var symbols = new SymbolMenu(); symbols.Open(english: true);
        check(string.Join("", symbols.View.Candidates.Select(c => c.Text)) == ".,?!@/-_#", "English punctuation occupies the first symbol page");
        symbols.Page(1); check(symbols.View.Candidates[0].Text == "，", "Chinese punctuation remains available after English symbols");
        symbols.Open(); check(symbols.View.Candidates[0].Text == "，", "Chinese mode retains its original symbol ordering");
        var blocked = false;
        try { NumericInput.SendDigit(InputMode.English, 1, 0); } catch (InvalidOperationException) { blocked = true; }
        check(blocked && NumericInput.SentKeyEvents == 0, "English input cannot simulate NumPad keys");
        foreach (var shoulder in new[] { Buttons.LB, Buttons.RB })
        {
            var buttons = new Controller(); buttons.Update(default, 0);
            var first = buttons.Update(new() { Buttons = shoulder }, 10).Single();
            var repeat = buttons.Update(new() { Buttons = shoulder }, 500).Single();
            check(!first.IsRepeat && repeat.IsRepeat && repeat.Action == first.Action, shoulder + ": repeat events remain distinguishable from a fresh case-toggle click");
            buttons.Update(default, 510);
            check(!buttons.Update(new() { Buttons = shoulder }, 520).Single().IsRepeat, shoulder + ": releasing then pressing produces a fresh click");
        }
    }
}
