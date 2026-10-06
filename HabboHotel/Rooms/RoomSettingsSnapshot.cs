using System.Collections.Immutable;

namespace Plus.HabboHotel.Rooms;

public sealed record RoomSettingsSnapshot(uint RoomId, string Name, string Description, int Access,
    int Category, int UsersMax, int CapacityLimit, ImmutableArray<string> Tags, int TradeSettings,
    bool AllowPets, bool AllowPetsEating, bool RoomBlockingEnabled, bool Hidewall, int WallThickness,
    int FloorThickness, int ChatMode, int ChatSize, int ChatSpeed, int ChatDistance, int ExtraFlood,
    int WhoCanMute, int WhoCanKick, int WhoCanBan)
{
    public static RoomSettingsSnapshot Capture(Room room) => new(room.RoomId, room.Name, room.Description,
        RoomAccessUtility.GetRoomAccessPacketNum(room.Access), room.Category, room.UsersMax,
        room.Model.MapSizeX * room.Model.MapSizeY > 100 ? 50 : 25, room.Tags.ToImmutableArray(),
        room.TradeSettings, room.AllowPets, room.AllowPetsEating, room.RoomBlockingEnabled, room.Hidewall,
        room.WallThickness, room.FloorThickness, room.ChatMode, room.ChatSize, room.ChatSpeed,
        room.ChatDistance, room.ExtraFlood, room.WhoCanMute, room.WhoCanKick, room.WhoCanBan);
}
