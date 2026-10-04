namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Rooms.Instance;

public sealed class RoomRuntimeComponent : IRoomComponent
{
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger), new(room), new(room), new(room),
        new(room, room.WiredLogger), new(room), new(room));
    public void Initiated() { }
}
