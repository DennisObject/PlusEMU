using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class GroupDeactivatedComposer : IServerPacket
{
    private readonly int _groupId;

    public uint MessageId => ServerPacketHeader.GroupDeactivatedComposer;

    public GroupDeactivatedComposer(int groupId)
    {
        _groupId = groupId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_groupId);
    }
}
