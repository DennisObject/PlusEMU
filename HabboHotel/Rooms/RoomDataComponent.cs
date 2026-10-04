namespace Plus.HabboHotel.Rooms;

public sealed class RoomDataComponent : IRoomComponent
{
    private Room? _room;

    public void Initiate(Room room) => _room = room;

    public void Initiated()
    {
        var room = _room!;
        room.GetRoomItemHandler().LoadFurniture();
        room.GetGameMap().GenerateMaps();
        room.LoadPromotions();
    }
}
