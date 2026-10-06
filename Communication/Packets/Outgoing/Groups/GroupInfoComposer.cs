using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupInfoComposer : IServerPacket
{
    private readonly GroupInfoSnapshot _info;
    private readonly bool _newWindow;

    public uint MessageId => ServerPacketHeader.GroupInfoComposer;

    public GroupInfoComposer(GroupInfoSnapshot info, bool newWindow = false)
    {
        _info = info;
        _newWindow = newWindow;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_info.Id);
        packet.WriteBoolean(true);
        packet.WriteInteger(_info.Type == GroupType.Open ? 0 : _info.Type == GroupType.Locked ? 1 : 2);
        packet.WriteString(_info.Name);
        packet.WriteString(_info.Description);
        packet.WriteString(_info.Badge);
        packet.WriteUInteger(_info.RoomId);
        packet.WriteString(_info.RoomName);
        packet.WriteInteger(_info.ViewerIsCreator ? 3 : _info.ViewerHasRequest ? 2 : _info.ViewerIsMember ? 1 : 0);
        packet.WriteInteger(_info.MemberCount); // Members
        packet.WriteBoolean(false); //?? CHANGED
        packet.WriteString(_info.CreatedOn);
        packet.WriteBoolean(_info.ViewerIsCreator);
        packet.WriteBoolean(_info.ViewerIsAdmin); // admin
        packet.WriteString(_info.CreatorName);
        packet.WriteBoolean(_newWindow); // Show group info
        packet.WriteBoolean(_info.AdminOnlyDecoOpen); // Any user can place furni in home room
        packet.WriteInteger(_info.PendingRequests); // Pending users
        //base.WriteInteger(0);//what the fuck
        packet.WriteBoolean(_info.ForumEnabled); //HabboTalk.
    }
}
