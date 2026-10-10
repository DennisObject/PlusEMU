using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.DataFormat;

namespace Plus.HabboHotel.Items;

public abstract record FurnitureDataSnapshot(FurniDataStructure Structure)
{
    public sealed record Empty() : FurnitureDataSnapshot(FurniDataStructure.Empty);
    public sealed record Legacy(string Value) : FurnitureDataSnapshot(FurniDataStructure.Legacy);
    public sealed record Map(ImmutableArray<KeyValuePair<string, string>> Values) : FurnitureDataSnapshot(FurniDataStructure.Map);
    public sealed record Strings(ImmutableArray<string> Values) : FurnitureDataSnapshot(FurniDataStructure.StringArray);
    public sealed record Vote(string State, int Result) : FurnitureDataSnapshot(FurniDataStructure.VoteResult);
    public sealed record Integers(ImmutableArray<int> Values) : FurnitureDataSnapshot(FurniDataStructure.IntArray);
    public sealed record Highscore(string State, uint ScoreType, uint ClearType) : FurnitureDataSnapshot(FurniDataStructure.HighScore)
    {
        public ImmutableArray<HighscoreEntry> Entries { get; init; } = [];
    }
    public sealed record Crackable(string State, uint Hits, uint Target) : FurnitureDataSnapshot(FurniDataStructure.Crackable);

    public static FurnitureDataSnapshot Capture(IFurniObjectData data) => data switch
    {
        EmptyDataFormat => new Empty(),
        LegacyDataFormat value => new Legacy(value.Data),
        MapDataFormat value => new Map(value.Data.ToImmutableArray()),
        StringArrayDataFormat value => new Strings(value.Data.ToImmutableArray()),
        VoteResultDataFormat value => new Vote(value.State, value.Result),
        IntArrayDataFormat value => new Integers(value.Data.ToImmutableArray()),
        HighscoreDataFormat value => new Highscore(value.State, value.ScoreType, value.ClearType) { Entries = value.CaptureEntries() },
        CrackableDataFormat value => new Crackable(value.State, value.Hits, value.Target),
        _ => throw new ArgumentOutOfRangeException(nameof(data), data.StructureType, "Unsupported furniture wire data.")
    };
}

public sealed record FurnitureMetadata(bool Stackable, bool IsSeat, bool IsBed, bool Walkable, int Width, int Length);

public sealed record RoomItemSnapshot(
    uint Id, int SpriteId, int X, int Y, int Rotation, string Z, string Height, int FloorExtra,
    FurnitureDataSnapshot Data, uint UniqueNumber, uint UniqueSeries, string WallCoordinates,
    string WallData, int UseButton, int UserId, string Username, FurnitureMetadata Metadata)
{
    public static RoomItemSnapshot Capture(Item item)
    {
        lock (item.NavSync) {
            return CaptureLocked(item);
        }
    }

    private static RoomItemSnapshot CaptureLocked(Item item)
    {
        var definition = item.Definition;
        var type = definition.InteractionType;
        var z = item.GetZ;
        var legacy = item.LegacyDataString;
        var extra = 1;

        if (type == InteractionType.WalkMagicTile) {
            extra = legacy.Split(';').Skip(1).FirstOrDefault() == "1" ? 1 : 0;
        }
        else if (type == InteractionType.Gift) {
            extra = GiftWrap.Style(legacy);
        }
        else if (type == InteractionType.MusicDisc) {
            var fields = legacy.Split('\n');

            if (fields.Length >= 7 && int.TryParse(fields[6], out var songId)) {
                extra = songId;
            }
        }

        var data = item.IsWallItem ? new FurnitureDataSnapshot.Empty() : MagicTileHeight.IsMagicTile(type)
            ? new FurnitureDataSnapshot.Legacy(MagicTileHeight.ToWire(z).ToString(CultureInfo.InvariantCulture))
            : FurnitureDataSnapshot.Capture(item.ExtraData);

        return new(item.Id, definition.SpriteId, item.GetX, item.GetY, item.Rotation,
            z.ToString(CultureInfo.InvariantCulture), definition.Height.ToString(CultureInfo.InvariantCulture), extra,
            data, item.UniqueNumber, item.UniqueSeries, item.WallCoordinates ?? "",
            type == InteractionType.Postit ? legacy.Split(' ')[0] : legacy,
            definition.Modes > 1 ? 1 : 0, item.UserId, item.Username ?? "",
            new(definition.Stackable, definition.IsSeat, type == InteractionType.Bed, definition.Walkable, definition.Width, definition.Length));
    }
}

public sealed record FurnitureOwner(int Id, string Name);
public sealed record RoomFurnitureSnapshot(ImmutableArray<RoomItemSnapshot> Items, ImmutableArray<FurnitureOwner> Owners)
{
    public static RoomFurnitureSnapshot Capture(IEnumerable<Item> items, int roomOwnerId, string? roomOwnerName)
    {
        var snapshots = items.Select(RoomItemSnapshot.Capture).ToImmutableArray();
        var names = new Dictionary<int, string>();

        foreach (var item in snapshots) {
            var name = item.UserId == roomOwnerId ? roomOwnerName ?? "" : item.Username;

            if (!names.TryGetValue(item.UserId, out var existing) || (existing.Length == 0 && name.Length > 0)) {
                names[item.UserId] = name;
            }
        }

        var owners = ImmutableArray.CreateBuilder<FurnitureOwner>();

        if (names.Remove(roomOwnerId, out var ownerName)) {
            owners.Add(new(roomOwnerId, ownerName));
        }

        foreach (var id in names.Keys.OrderBy(id => id)) {
            owners.Add(new(id, names[id]));
        }

        return new(snapshots, owners.ToImmutable());
    }
}
