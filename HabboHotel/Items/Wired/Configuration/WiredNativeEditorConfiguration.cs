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
    public bool? LegacySaysBool { get; init; }
    public string? LegacySaysItemsData { get; init; }
    public bool? LegacyRotateBool { get; init; }
    public string? LegacyRotateItemsData { get; init; }
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

// Exact-empty concrete Says proof; no StoredLegacy authority is minted on editor open.
internal sealed record LegacySaysSnapshot(
    Plus.HabboHotel.Items.Wired.Boxes.Triggers.UserSaysBox Box, Item Item,
    Plus.HabboHotel.Rooms.Room Room, ItemDefinition Definition, WiredBoxDescriptor Descriptor,
    System.Collections.Concurrent.ConcurrentDictionary<uint, Item> Dictionary,
    ImmutableArray<KeyValuePair<uint, Item>> Picks, bool BoolData, string ItemsData,
    WiredNativeEditorConfiguration Native)
{
    internal bool Matches() => ReferenceEquals(Box.Item, Item) && ReferenceEquals(Box.Instance, Room)
        && ReferenceEquals(Item.GetRoom(), Room) && Item.RoomId == Room.Id && ReferenceEquals(Item.Definition, Definition)
        && ReferenceEquals(Room.GetRoomItemHandler().GetItem(Item.Id), Item) && !Item.IsTemporary
        && Item.Definition.WiredType != WiredBoxType.TriggerUserSaysCommand
        && WiredLegacyEditorProjection.TryGetDescriptor(Box, out var current) && ReferenceEquals(current, Descriptor)
        && Box.StringData == "" && Box.BoolData == BoolData && Box.ItemsData == ItemsData
        && ReferenceEquals(Box.SetItems, Dictionary) && Dictionary.Count == Picks.Length
        && Picks.All(pick => Dictionary.TryGetValue(pick.Key, out var value) && ReferenceEquals(value, pick.Value)
            && ReferenceEquals(Room.GetRoomItemHandler().GetItem(pick.Key), pick.Value)
            && ReferenceEquals(pick.Value.GetRoom(), Room) && pick.Value.RoomId == Room.Id && !pick.Value.IsTemporary && pick.Value.IsFloorItem);
}

// These proofs exist only for one editor request; opening a card installs no configuration authority.
internal sealed record NativeMovementPick(WiredNativeItemReference Reference, Item Item, ItemDefinition Definition, long Placement, long Movement, int Rotation)
{
    internal bool Matches(Plus.HabboHotel.Rooms.Room room) => Reference.ItemId == Item.Id
        && Reference.Wall == Item.IsWallItem && !Item.IsTemporary && Item.Placement == Placement
        && Item.MovementGeneration == Movement && Item.Rotation == Rotation
        && ReferenceEquals(Item.Definition, Definition) && ReferenceEquals(Item.GetRoom(), room) && Item.RoomId == room.Id
        && ReferenceEquals(room.GetRoomItemHandler().GetItem(Item.Id), Item);
}

internal sealed record FreshDirectionSnapshot(
    Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernAction Box, WiredConfiguration Configuration,
    Item Item, Plus.HabboHotel.Rooms.Room Room, uint RoomId, ItemDefinition Definition,
    WiredBoxDescriptor Descriptor, long Placement, long Movement, int Rotation, WiredNativeEditorConfiguration Native,
    WiredNativeEditorConfiguration? Request, ImmutableArray<NativeMovementPick> RequestedPicks)
{
    internal bool Matches() => Box.HasInitialDirectionDraft && ReferenceEquals(Box.Configuration, Configuration)
        && ReferenceEquals(Box.Item, Item) && ReferenceEquals(Box.Instance, Room) && Room.Id == RoomId
        && ReferenceEquals(Item.GetRoom(), Room) && Item.RoomId == RoomId && !Item.IsTemporary && Item.IsFloorItem
        && Item.Placement == Placement && Item.MovementGeneration == Movement && Item.Rotation == Rotation
        && ReferenceEquals(Item.Definition, Definition)
        && ReferenceEquals(Box.Descriptor, Descriptor) && Item.Definition.WiredDescriptor?.CanonicalName == Descriptor.CanonicalName
        && ReferenceEquals(Room.GetRoomItemHandler().GetItem(Item.Id), Item)
        && RequestedPicks.All(pick => pick.Matches(Room));
}

internal sealed record LegacyRotateSnapshot(
    Plus.HabboHotel.Items.Wired.Boxes.Effects.MoveAndRotateBox Box, Item Item,
    Plus.HabboHotel.Rooms.Room Room, ItemDefinition Definition, WiredBoxDescriptor Descriptor, long Placement,
    System.Collections.Concurrent.ConcurrentDictionary<uint, Item> Dictionary,
    ImmutableArray<KeyValuePair<uint, Item>> Picks, bool BoolData, string ItemsData, int Delay,
    WiredNativeEditorConfiguration Native)
{
    internal bool Matches() => ReferenceEquals(Box.Item, Item) && ReferenceEquals(Box.Instance, Room)
        && ReferenceEquals(Item.GetRoom(), Room) && Item.RoomId == Room.Id && Item.Placement == Placement
        && ReferenceEquals(Item.Definition, Definition) && !Item.IsTemporary && Item.IsFloorItem
        && ReferenceEquals(Room.GetRoomItemHandler().GetItem(Item.Id), Item)
        && WiredLegacyEditorProjection.TryGetDescriptor(Box, out var current) && ReferenceEquals(current, Descriptor)
        && Box.StringData == "" && Box.BoolData == BoolData && Box.ItemsData == ItemsData && Box.Delay == Delay
        && ReferenceEquals(Box.SetItems, Dictionary) && Dictionary.Count == Picks.Length
        && Picks.All(pick => pick.Key == pick.Value.Id && Dictionary.TryGetValue(pick.Key, out var value)
            && ReferenceEquals(value, pick.Value) && ReferenceEquals(Room.GetRoomItemHandler().GetItem(pick.Key), value)
            && ReferenceEquals(value.GetRoom(), Room) && value.RoomId == Room.Id && !value.IsTemporary && value.IsFloorItem);
}

internal sealed record FreshCardSnapshot(Plus.HabboHotel.Items.Wired.Modern.Actions.WiredModernBox Box,
    WiredConfiguration Configuration, Plus.HabboHotel.Rooms.Room Room, uint RoomId, PristineCardPick Identity,
    WiredBoxDescriptor Descriptor, WiredBoxDescriptor DefinitionDescriptor,
    System.Collections.Concurrent.ConcurrentDictionary<uint, Item> Dictionary, (long Started, long Epoch)? Timing,
    WiredNativeEditorConfiguration Native, WiredNativeEditorConfiguration? Request, ImmutableArray<PristineCardPick> RequestedPicks)
{
    internal bool Matches() => WiredNativeEditorProjection.HasInitialCardConfiguration(Box)
        && ReferenceEquals(Box.Configuration, Configuration) && ReferenceEquals(Box.Item, Identity.Pose.Item)
        && ReferenceEquals(Box.Instance, Room) && Room.Id == RoomId && Identity.Matches(Room) && Box.Item.IsFloorItem
        && ReferenceEquals(Box.Descriptor, Descriptor) && ReferenceEquals(Box.Item.Definition.WiredDescriptor, DefinitionDescriptor)
        && Box.StringData == "" && Box.ItemsData == "" && !Box.BoolData
        && ReferenceEquals(Box.SetItems, Dictionary) && Dictionary.Count == 0
        && (Box is not Plus.HabboHotel.Items.Wired.Modern.Triggers.WiredModernTimedTrigger timer || timer.InitialCardTiming == Timing)
        && RequestedPicks.All(pick => pick.Matches(Room) && pick.Pose.Item.IsFloorItem);
}

// Closed pristine-card proof for one request. No runtime authority is installed by capture/open.
internal sealed record PristineCardPick(NativeMovementPick Pose, uint DefinitionId,
    Plus.HabboHotel.Users.Inventory.Furniture.ItemType Kind, InteractionType Interaction, WiredBoxType WiredType,
    int X, int Y, long ZBits)
{
    internal static PristineCardPick Capture(Item item) => new(
        new(new(item.Id, item.IsWallItem), item, item.Definition, item.Placement, item.MovementGeneration, item.Rotation),
        item.Definition.Id, item.Definition.Type, item.Definition.InteractionType, item.Definition.WiredType,
        item.GetX, item.GetY, BitConverter.DoubleToInt64Bits(item.GetZ));

    internal bool Matches(Plus.HabboHotel.Rooms.Room room) => Pose.Matches(room)
        && Pose.Item.Definition.Id == DefinitionId && Pose.Item.Definition.Type == Kind
        && Pose.Item.Definition.InteractionType == Interaction && Pose.Item.Definition.WiredType == WiredType
        && Pose.Item.GetX == X && Pose.Item.GetY == Y && BitConverter.DoubleToInt64Bits(Pose.Item.GetZ) == ZBits;
}

internal sealed record PristineCardSnapshot(IWiredItem Box, Item Item, Plus.HabboHotel.Rooms.Room Room,
    uint RoomId, PristineCardPick Identity, WiredBoxDescriptor Descriptor,
    System.Collections.Concurrent.ConcurrentDictionary<uint, Item> Dictionary,
    WiredNativeEditorConfiguration Native, WiredNativeEditorConfiguration? Request,
    ImmutableArray<PristineCardPick> RequestedPicks)
{
    internal bool Matches() => ReferenceEquals(Box.Item, Item) && ReferenceEquals(Box.Instance, Room)
        && Room.Id == RoomId && Identity.Matches(Room) && Item.IsFloorItem
        && WiredNativeEditorProjection.IsPristineCard(Box, out var current) && ReferenceEquals(current, Descriptor)
        && ReferenceEquals(Box.SetItems, Dictionary) && Dictionary.Count == 0
        && RequestedPicks.All(pick => pick.Matches(Room) && pick.Pose.Item.IsFloorItem);
}
