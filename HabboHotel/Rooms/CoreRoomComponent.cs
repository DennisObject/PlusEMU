namespace Plus.HabboHotel.Rooms;

public sealed class CoreRoomComponent : IRoomComponent
{
    private Room? _room;

    public void Initiate(Room room)
    {
        _room = room;
        room.InitializeCore();
    }

    public void Initiated() => _room!.LoadCoreData();
}
