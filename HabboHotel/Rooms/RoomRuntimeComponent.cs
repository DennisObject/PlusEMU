namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Rooms.Instance;

public sealed class RoomRuntimeComponent : IRoomComponent
{
    public int Order => 0;
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger), new(room), new(room), new(room), new(room, room.WiredLogger));
    public void Initiated() { }
}
