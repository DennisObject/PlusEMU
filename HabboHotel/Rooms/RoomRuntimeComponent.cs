namespace Plus.HabboHotel.Rooms;

public sealed class RoomRuntimeComponent : IRoomComponent
{
    public void Initiate(Room room) => room.InitializeRuntime();
    public void Initiated() { }
}
