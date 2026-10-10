using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Plus.HabboHotel.Items.Wired.Configuration;

/// <summary>The single stored authority for a canonical native editor save.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WiredNativeEditorConfiguration
{
    public int Version { get; init; } = 2;
    public WiredBoxCategory Category { get; init; }
    public int NativeCode { get; init; }
    public ImmutableArray<int> OwnedIntParams { get; init; } = [];
    public string Text { get; init; } = "";
    public ImmutableArray<WiredNativeItemReference> PrimaryItems { get; init; } = [];
    public ImmutableArray<WiredNativeItemReference> SecondaryItems { get; init; } = [];
    public ImmutableArray<int> FurniSourceTypes { get; init; } = [];
    public ImmutableArray<int> UserSourceTypes { get; init; } = [];
    public ImmutableArray<string> VariableIds { get; init; } = [];
    public int? Delay { get; init; }
    public int? Quantifier { get; init; }
    public bool? Filter { get; init; }
    public bool? Inverse { get; init; }
    public WiredNativeSavedState SavedState { get; init; } = new();
    public WiredNativeDormantLegacy? DormantLegacy { get; init; }
}

public sealed record WiredNativeItemReference(uint ItemId, bool Wall)
{
    [JsonIgnore]
    public int WireId => Wall ? checked(-(int)ItemId) : checked((int)ItemId);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WiredNativeSavedState
{
    public ImmutableArray<WiredFurniSnapshot> Snapshots { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record WiredNativeDormantLegacy
{
    public int? JoinMode { get; init; }
    public string? LegacyJoinTeam { get; init; }
    public bool? LegacyJoinBool { get; init; }
    public string? LegacyJoinItemsData { get; init; }
    public int? GroupDirection { get; init; }
    public ImmutableDictionary<string, int> FurniSources { get; init; } = ImmutableDictionary<string, int>.Empty;
    public ImmutableDictionary<string, int> UserSources { get; init; } = ImmutableDictionary<string, int>.Empty;
    public string Text { get; init; } = "";
}

internal enum WiredConfigurationOriginKind
{
    StoredLegacy, Native
}

internal sealed record WiredConfigurationOrigin(WiredConfigurationOriginKind Kind, uint ItemId, string Name,
    WiredConfiguration Derived, WiredNativeEditorConfiguration? Native, WiredConfiguration? StoredLegacy = null);

public enum WiredNativeSaveAdmission
{
    Unchanged, Changed, Refused
}

public sealed record WiredNativeEditorMetadata(ImmutableArray<ImmutableArray<int>> FurniAllowed,
    ImmutableArray<ImmutableArray<int>> UsersAllowed, ImmutableArray<int> FurniDefaults,
    ImmutableArray<int> UserDefaults, ImmutableArray<int> OwnedDefaults, bool AllowWall);

// Request-local proof only. This is neither a persisted V1 record nor a runtime cache authority.
internal sealed record LegacyJoinSnapshot(
    Plus.HabboHotel.Items.Wired.Boxes.Effects.AddActorToTeamBox Box, Item Item,
    Plus.HabboHotel.Rooms.Room Room, ItemDefinition Definition, WiredBoxDescriptor Descriptor,
    System.Collections.Concurrent.ConcurrentDictionary<uint, Item> Dictionary,
    ImmutableArray<KeyValuePair<uint, Item>> Picks, string TeamText, bool BoolData, string ItemsData,
    WiredNativeEditorConfiguration Native)
{
    internal bool Fresh => TeamText.Length == 0;

    internal bool Matches() => ReferenceEquals(Box.Item, Item) && ReferenceEquals(Box.Instance, Room)
        && ReferenceEquals(Item.GetRoom(), Room) && Item.RoomId == Room.Id && ReferenceEquals(Item.Definition, Definition)
        && ReferenceEquals(Room.GetRoomItemHandler().GetItem(Item.Id), Item) && !Item.IsTemporary
        && WiredLegacyEditorProjection.TryGetDescriptor(Box, out var current) && ReferenceEquals(current, Descriptor)
        && Box.StringData == TeamText && Box.BoolData == BoolData && Box.ItemsData == ItemsData
        && ReferenceEquals(Box.SetItems, Dictionary) && Dictionary.Count == Picks.Length
        && Picks.All(pick => Dictionary.TryGetValue(pick.Key, out var value) && ReferenceEquals(value, pick.Value)
            && ReferenceEquals(Room.GetRoomItemHandler().GetItem(pick.Key), pick.Value)
            && ReferenceEquals(pick.Value.GetRoom(), Room) && pick.Value.RoomId == Room.Id && !pick.Value.IsTemporary && pick.Value.IsFloorItem);
}
