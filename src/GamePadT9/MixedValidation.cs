using System.Text.Json;

namespace GamePadT9;

internal static class MixedValidation
{
    internal static Settings Prepare(string root)
    {
        var original = Settings.Load(root);
        var isolated = original with { UserPath = Path.Combine(root, "artifacts", "mixed-validation", "rime-user") };
        Directory.CreateDirectory(isolated.UserPath);
        var script = Path.Combine(original.UserPath, "rime.lua");
        if (File.Exists(script)) File.Copy(script, Path.Combine(isolated.UserPath, "rime.lua"), true);
        var lua = Path.Combine(original.UserPath, "lua");
        if (Directory.Exists(lua))
            foreach (var file in Directory.EnumerateFiles(lua, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(isolated.UserPath, "lua", Path.GetRelativePath(lua, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
        return isolated;
    }
    internal static int Run(string root, RimeEngine engine)
    {
        List<string> checks = [];
        void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException("FAILED: " + name + " / " + JsonSerializer.Serialize(engine.View));
            checks.Add(name);
        }
        var pad = new Controller(); pad.Update(default, 0);
        var state = new Gamepad { RY = 22000, LX = -20000, LY = 20000 };
        Check(pad.Update(state, 10).Count == 0 && pad.Region == 1 && pad.Detail == 3, "ABC upper-left blank highlights without typing");
        state.RT = 200;
        var blank = pad.Update(state, 20).Single();
        Check(T9Layout.Key(blank) == 0 && blank.Detail == 3, "Trigger on the blank falls back to the group");
        engine.Input(blank); Check(engine.View.Preedit.Length > 0 && engine.PendingCommit.Length == 0, "Blank fallback reaches Rime as an ambiguous group"); engine.Clear();
        Check(pad.Update(state, 30).Count == 0, "Holding the detail trigger never repeats");
        state.RT = 0; state.LX = 12000; state.LY = 0; pad.Update(state, 40);
        Check(pad.Detail == 0, "Detail remains expanded between the radial thresholds");
        state.LX = 10000; pad.Update(state, 50); Check(pad.Detail == -1, "Returning below the inner threshold collapses the cell");
        state.LX = 14000; pad.Update(state, 60); Check(pad.Detail == -1, "The stick must cross the outer threshold to reopen");
        state.LX = 20000; state.LY = 20000; pad.Update(state, 70);
        state.LX = -1000; pad.Update(state, 80); Check(pad.Detail == 0, "Axis jitter preserves the selected quadrant");
        state.LX = -5000; pad.Update(state, 90); Check(pad.Detail == 3, "Crossing the angular dead zone selects the neighboring quadrant");
        var clock = 100L;
        (short X, short Y)[] directions = [(22000, 22000), (22000, -22000), (-22000, -22000), (-22000, 22000)];
        for (var group = 1; group < 9; group++)
        for (var detail = 0; detail < 4; detail++)
        {
            state = new() { RX = (short)((group % 3 - 1) * 22000), RY = (short)((1 - group / 3) * 22000), LX = directions[detail].X, LY = directions[detail].Y };
            pad.Update(state, clock++); state.RT = 200; var action = pad.Update(state, clock++).Single();
            Check(action.Region == group && action.Detail == detail && T9Layout.Key(action) ==
                (detail < T9Layout.Groups[group].Length ? char.ToLowerInvariant(T9Layout.Groups[group][detail]) : 0), $"Physical stick/trigger mapping group {group}, quadrant {detail + 1}");
        }
        pad.Configure(new() { Stick = ControlSide.Left, Trigger = ControlSide.Left }); pad.Update(default, clock++);
        var swapped = pad.Update(new() { LY = 22000, RX = 22000, RY = -22000, LT = 200 }, clock++).Single();
        Check(swapped.Region == 1 && swapped.Detail == 1 && T9Layout.Key(swapped) == 'b', "Swapping the configured group stick also swaps the detail stick");
        pad.Update(new() { LY = 22000, RX = 22000, RY = -22000 }, clock++);
        var click = pad.Update(new() { LY = 22000, RX = 22000, RY = -22000, Buttons = Buttons.L3 }, clock++).Single();
        Check(click.StickClick && click.Detail == -1 && T9Layout.Key(click) == 0, "Clicking the group stick preserves whole-group input and numeric zero");
        pad.Reset(); Check(pad.Detail == -1, "Reset and disconnect discard the detail selection");
        const string spelling = "nihao";
        int[] regions = [5, 3, 3, 1, 5];
        for (var mask = 0; mask < 32; mask++)
        {
            engine.Clear();
            for (var i = 0; i < spelling.Length; i++)
                if ((mask & (1 << i)) != 0) engine.InputLetter(spelling[i]); else engine.InputRegion(regions[i]);
            Check(engine.PendingCommit.Length == 0 && Validation.Find(engine, "你好"), $"nihao exact/group combination {mask}");
            engine.Confirm();
            Check(engine.PendingCommit == "你好", $"nihao commit {mask}");
            engine.AcknowledgeCommit();
        }
        // Retain the source schema's fuzzy spelling and abbreviations; compare
        // distinct initials within MNO rather than forbidding its fuzzy results.
        foreach (var wrong in new[] { "mihao", "oihao" })
        {
            engine.Clear(); foreach (var letter in wrong) engine.InputLetter(letter);
            Check(!Validation.Find(engine, "你好"), "Exact letters exclude an incompatible spelling: " + wrong);
        }
        for (var mask = 0; mask < 8; mask++)
        {
            engine.Clear();
            for (var i = 0; i < 3; i++) if ((mask & (1 << i)) == 0) engine.InputRegion(i == 1 ? 1 : 5); else engine.InputLetter("nan"[i]);
            Check(Validation.Find(engine, "南"), "Repeated letters can independently mix with their group: nan " + mask);
        }
        engine.Clear(); engine.InputLetter('n'); engine.InputRegion(3); engine.Process(0xFF08); engine.InputLetter('i');
        Check(Validation.Find(engine, "你"), "Backspace removes one mixed token before a precise replacement");
        foreach (var (detail, text) in new[] { (2, "，"), (1, "。") })
        {
            engine.Clear(); engine.Input(new(PadAction.Region, 0, Detail: detail));
            Check(engine.PendingCommit == text, "Symbol detail commits directly: " + text); engine.AcknowledgeCommit();
        }
        engine.Clear(); foreach (var letter in spelling) engine.InputLetter(letter); Validation.Find(engine, "你好");
        engine.Input(new(PadAction.Region, 0, Detail: 2));
        Check(engine.PendingCommit == "你好，" && engine.View.Preedit.Length == 0, "Comma after a composition confirms the selected word and punctuation together");
        Check(NumericInput.SentKeyEvents == 0, "All mixed composition and punctuation stay outside the OS keyboard queue");
        engine.Clear();
        var report = new { passed = true, checks };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, "artifacts", "mixed-validation.json"), json);
        Console.WriteLine(json); return 0;
    }
}
