namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Rooms.Instance;

public sealed class RoomRuntimeComponent(IRoomItemStore itemStore, IRoomUserStore userStore,
    IRoomUserSnapshotService userSnapshots) : IRoomComponent
{
    public int Order => 0;
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger), new(room, itemStore), new(room, userStore), new(room),
        new(room, room.WiredLogger), userSnapshots);
    public void Initiated() { }
}
