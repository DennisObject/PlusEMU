namespace Plus.HabboHotel.Rooms;

public sealed record RoomEventSnapshot(int RoomId, int OwnerId, string OwnerName, bool Active, string Name, string Description)
{
    public static RoomEventSnapshot Capture(RoomData data, RoomPromotion? promotion) => promotion == null
        ? new(-1, -1, string.Empty, false, string.Empty, string.Empty)
        : new(checked((int)data.Id), data.OwnerId, data.OwnerName, true, promotion.Name, promotion.Description);
}
