using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupFurniSettingsComposer : IServerPacket
{
    private readonly GroupFurniSettingsSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.GroupFurniSettingsComposer;

    public GroupFurniSettingsComposer(GroupFurniSettingsSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInteger(_snapshot.ItemId); //Item Id
        packet.WriteInteger(_snapshot.GroupId); //Group Id?
        packet.WriteString(_snapshot.Name);
        packet.WriteUInteger(_snapshot.RoomId); //RoomId
        packet.WriteBoolean(_snapshot.IsMember); //Member?
        packet.WriteBoolean(_snapshot.ForumEnabled); //Has a forum
    }
}