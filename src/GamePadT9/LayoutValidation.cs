namespace GamePadT9;

internal static class LayoutValidation
{
    internal static void Run(Action<bool, string> check)
    {
        check(LetterLayouts.Order(LetterLayout.Default, "1234") == "4132" &&
            LetterLayouts.Order(LetterLayout.Clockwise, "1234") == "1243" && LetterLayouts.Order(LetterLayout.Rows, "4132") == "1234",
            "The three presets match blank-A/C-B, A-B/blank-C and A-B/C-blank");
        var orders = from a in "1234" from b in "1234" from c in "1234" from d in "1234"
                     let order = new string([a, b, c, d]) where LetterLayouts.Valid(order) select order;
        int[] quadrants = [3, 0, 2, 1];
        foreach (var order in orders)
        {
            var settings = new UserSettings { PinyinLayout = LetterLayout.Custom, PinyinCustomOrder = order, EnglishLayout = LetterLayout.Rows };
            var linked = settings with { EnglishUsePinyinLayout = true };
            var valid = true;
            for (var group = 1; group < 9; group++)
            for (var position = 0; position < 4; position++)
            {
                var ordinal = order[position] - '1';
                var expected = ordinal < T9Layout.Groups[group].Length ? T9Layout.Groups[group][ordinal].ToString() : "";
                var action = new PadEvent(PadAction.Region, group, Detail: quadrants[position]);
                var chinese = T9Layout.Resolve(action, settings, InputMode.T9);
                var english = T9Layout.Resolve(action, linked, InputMode.English);
                valid &= T9Layout.DisplayLabel(group, action.Detail, settings, InputMode.T9) == expected &&
                    T9Layout.EnglishText(english, uppercase: true) == expected && T9Layout.EnglishText(english) == expected.ToLowerInvariant() &&
                    T9Layout.Key(chinese) == (expected.Length == 0 ? 0 : char.ToLowerInvariant(expected[0])) &&
                    T9Layout.Resolve(action, settings, InputMode.English).Detail == position &&
                    T9Layout.Resolve(action, linked, InputMode.Numeric) == action;
            }
            check(valid, "All letter groups, both cases, independent English and numeric mode map correctly for custom order " + order);
            check(Enumerable.Range(0, 4).All(q =>
            {
                var symbol = new PadEvent(PadAction.Region, 0, Detail: q);
                return T9Layout.Resolve(symbol, linked, InputMode.T9) == symbol && T9Layout.Resolve(symbol, linked, InputMode.English) == symbol;
            }), "Symbol subdivisions stay fixed for order " + order);
        }
        check(LetterLayouts.Swap("4132", 0, 1) == "1432" && LetterLayouts.Swap("1432", 0, 1) == "4132" && LetterLayouts.Swap("4132", 0, 0) == "4132",
            "Dragging swaps exactly two positions and supports undo by swapping back");
    }
}
