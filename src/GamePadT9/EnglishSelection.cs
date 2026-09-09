namespace GamePadT9;

// Shared by controller sampling, mouse input and the session. Opening or
// leaving a group is a UI operation; only a resolved letter reaches the target.
internal sealed class EnglishSelection
{
    internal int? Group { get; private set; }
    internal bool Uppercase { get; private set; }
    internal void ToggleCase() => Uppercase = !Uppercase;
    internal void Reset() => Group = null;
    internal bool Consume(PadEvent action)
    {
        if (action.Action == PadAction.Cancel && Group != null) { Reset(); return true; }
        if (action.Action != PadAction.Region || (uint)action.Region >= 9) return false;
        if (Group != null && action.Region != Group) Reset(); // Mouse selected another group.
        if (action.Detail >= 0 || action.Region == 0) return false;
        Group = Group == null ? action.Region : null;
        return true;
    }
}
