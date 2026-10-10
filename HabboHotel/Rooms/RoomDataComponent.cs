namespace Plus.HabboHotel.Rooms;

public sealed class RoomDataComponent(IRoomFurnitureLoader furniture) : IRoomComponent
{
    public int Order => 100;
    private Room? _room;

    public void Initiate(Room room) => _room = room;

    public void Initiated()
    {
        var room = _room!;
        room.GetRoomItemHandler().LoadFurniture(furniture.Load(room.Id));
        room.GetGameMap().GenerateMaps();
        room.GetWired().InitializeHighscores();
    }
}
