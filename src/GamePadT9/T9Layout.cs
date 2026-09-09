namespace GamePadT9;

internal static class T9Layout
{
    internal static readonly string[] Groups = ["符号", "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ"];
    // Clockwise from the upper right: 1, 2, 3, 4. -1 means the stick is centered.
    internal static int Quadrant(bool right, bool up) => up ? (right ? 0 : 3) : (right ? 1 : 2);
    private static int OrderedDetail(int region, int detail, UserSettings settings, InputMode mode)
        => region == 0 || (uint)detail >= 4 || mode == InputMode.Numeric ? detail :
            LetterLayouts.Slot(mode == InputMode.English ? settings.EnglishOrder : settings.PinyinOrder, detail);
    // Resolve physical quadrants exactly once at the session boundary. The
    // engine continues receiving letter indices, independent of presentation.
    internal static PadEvent Resolve(PadEvent action, UserSettings settings, InputMode mode)
        => action.Action == PadAction.Region ? action with { Detail = OrderedDetail(action.Region, action.Detail, settings, mode) } : action;
    internal static string DisplayLabel(int region, int detail, UserSettings settings, InputMode mode)
        => Label(region, OrderedDetail(region, detail, settings, mode), mode == InputMode.English);
    internal static string Label(int region, int detail, bool english = false)
    {
        if ((uint)region >= 9 || (uint)detail >= 4) return "";
        if (region == 0) return detail is 0 or 3 ? "符号" : detail == 2 ? (english ? "," : "，") : (english ? "." : "。");
        return detail < Groups[region].Length ? Groups[region][detail].ToString() : "";
    }
    internal static int Key(PadEvent action)
    {
        if (action.StickClick || (uint)action.Detail >= 4) return 0;
        if (action.Region == 0) return action.Detail == 2 ? ',' : action.Detail == 1 ? '.' : 0;
        var label = Label(action.Region, action.Detail);
        return label.Length == 0 ? 0 : char.ToLowerInvariant(label[0]);
    }
    internal static string EnglishText(PadEvent action, bool uppercase = false)
    {
        var label = Label(action.Region, action.Detail, english: true);
        return label.Length == 1 ? (uppercase ? label.ToUpperInvariant() : label.ToLowerInvariant()) : "";
    }
}
