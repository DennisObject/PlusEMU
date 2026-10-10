using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    private readonly HashSet<uint> _clickUserTriggers = [];

    private void UpdateClickEnvironment(uint id, IWiredItem? item = null)
    {
        var hadTrigger = _clickUserTriggers.Count != 0;

        if (item is IWiredConfiguredItem { Descriptor.CanonicalName: "wf_trg_click_user" }) {
            _clickUserTriggers.Add(id);
        }
        else {
            _clickUserTriggers.Remove(id);
        }

        if (hadTrigger == (_clickUserTriggers.Count != 0)) {
            return;
        }

        foreach (var viewer in _fxViewers.Keys) {
            if (ReferenceEquals(_room.GetRoomUserManager().GetRoomUserByVirtualId(viewer.VirtualId), viewer)) {
                viewer.GetClient()?.Send(new WiredEnvironmentComposer(_clickUserTriggers.Count != 0, _room.Id));
            }
        }
    }
}
