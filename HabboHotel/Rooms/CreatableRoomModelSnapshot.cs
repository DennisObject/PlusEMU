using System.Collections.Immutable;

namespace Plus.HabboHotel.Rooms;

public sealed record CreatableRoomModelSnapshot(string Id, int TileSize, int MapSizeX, int MapSizeY, int RequiredClubLevel)
{
    public static ImmutableArray<CreatableRoomModelSnapshot> Capture(IEnumerable<RoomModel> models) =>
        models.Select(model => new CreatableRoomModelSnapshot(model.Id, model.TileSize,
            model.MapSizeX, model.MapSizeY, model.RequiredClubLevel)).ToImmutableArray();
}
