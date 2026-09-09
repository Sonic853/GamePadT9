using System.Text.Json.Serialization;

namespace GamePadT9;

[JsonConverter(typeof(JsonStringEnumConverter<LetterLayout>))]
internal enum LetterLayout { Default, Clockwise, Rows, Custom }

internal static class LetterLayouts
{
    // Row order: upper left, upper right, lower left, lower right.
    // 4 is blank for ABC and the fourth letter for PQRS/WXYZ.
    internal const string DefaultOrder = "4132";
    internal static string Order(LetterLayout layout, string custom) => layout switch
    {
        LetterLayout.Default => DefaultOrder,
        LetterLayout.Clockwise => "1243",
        LetterLayout.Rows => "1234",
        LetterLayout.Custom => custom,
        _ => throw new InvalidDataException("四格排序选项无效。")
    };
    internal static bool Valid(string? order) => order is { Length: 4 } && order.Order().SequenceEqual("1234");
    internal static int Slot(string order, int quadrant) => order[quadrant switch { 0 => 1, 1 => 3, 2 => 2, _ => 0 }] - '1';
    internal static string Swap(string order, int source, int target)
    {
        if (!Valid(order) || (uint)source >= 4 || (uint)target >= 4) throw new ArgumentOutOfRangeException(nameof(order));
        var slots = order.ToCharArray(); (slots[source], slots[target]) = (slots[target], slots[source]);
        return new string(slots);
    }
}
