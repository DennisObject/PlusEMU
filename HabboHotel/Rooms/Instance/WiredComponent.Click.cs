using Plus.HabboHotel.Items.Wired.Runtime;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    public WiredClickResult DispatchClickUser(RoomUser actor, RoomUser target) => _engine.DispatchClickUser(actor, target);
}
