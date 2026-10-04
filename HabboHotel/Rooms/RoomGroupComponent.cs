using Plus.HabboHotel.Groups;

namespace Plus.HabboHotel.Rooms;

public sealed class RoomGroupComponent(IGroupManager groups) : IRoomComponent
{
    private Room _room = null!;
    public int Order => 110;
    public void Initiate(Room room) => _room = room;
    public void Initiated()
    {
        if (_room.Data.GroupId > 0 && groups.TryGetGroup(_room.Data.GroupId, out var group)) _room.Group = group;
    }
}
