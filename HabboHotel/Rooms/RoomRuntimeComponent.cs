namespace Plus.HabboHotel.Rooms;

using Plus.HabboHotel.Rooms.Instance;

public sealed class RoomRuntimeComponent(IRoomItemStore itemStore, IRoomUserStore userStore,
    IRoomUserSnapshotService userSnapshots, TimeProvider clock) : IRoomComponent
{
    public int Order => 0;
    public void Initiate(Room room) => room.SetRuntime(
        new(room, room.Data.Model, room.NavigationLogger), new(room, itemStore), new(room, userStore, clock), new(room),
        new(room, room.WiredLogger, clock), userSnapshots);
    public void Initiated() { }
}
