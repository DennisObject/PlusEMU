namespace Plus.HabboHotel.Items.Wired.Runtime;

// Avatar IDs are room VirtualIds. Durable storage must explicitly convert to stable Habbo IDs.
public sealed class WiredSelection(IEnumerable<uint>? furniIds = null, IEnumerable<int>? userIds = null)
{
    public HashSet<uint> FurniIds { get; } = furniIds?.ToHashSet() ?? [];
    public HashSet<int> UserIds { get; } = userIds?.ToHashSet() ?? [];
    public WiredSelection Copy() => new(FurniIds, UserIds);
}

[Flags]
public enum WiredSelectionKind
{
    Furni = 1, Users = 2, Both = Furni | Users
}

// The engine applies these wrappers once, replacing only the selected kind when filtering.
public sealed record WiredSelectorResult(WiredSelection Selection, WiredSelectionKind Kind,
    bool FiltersExisting = false, bool Invert = false);

public sealed class WiredSignalPayload(WiredSelection selection, IReadOnlyDictionary<string, long> values)
{
    public WiredSelection Selection { get; } = selection.Copy();
    public IReadOnlyDictionary<string, long> Values { get; } = new Dictionary<string, long>(values);
}
