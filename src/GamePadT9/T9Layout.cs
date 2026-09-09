namespace GamePadT9;

internal static class T9Layout
{
    internal static readonly string[] Groups = ["符号", "ABC", "DEF", "GHI", "JKL", "MNO", "PQRS", "TUV", "WXYZ"];
    // Clockwise from the upper right: 1, 2, 3, 4. -1 means the stick is centered.
    internal static int Quadrant(bool right, bool up) => up ? (right ? 0 : 3) : (right ? 1 : 2);
    internal static string Label(int region, int detail)
    {
        if ((uint)region >= 9 || (uint)detail >= 4) return "";
        if (region == 0) return detail is 0 or 3 ? "符号" : detail == 2 ? "，" : "。";
        return detail < Groups[region].Length ? Groups[region][detail].ToString() : "";
    }
    internal static int Key(PadEvent action)
    {
        if (action.StickClick || (uint)action.Detail >= 4) return 0;
        if (action.Region == 0) return action.Detail == 2 ? ',' : action.Detail == 1 ? '.' : 0;
        var label = Label(action.Region, action.Detail);
        return label.Length == 0 ? 0 : char.ToLowerInvariant(label[0]);
    }
}
