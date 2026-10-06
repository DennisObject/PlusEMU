using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Groups;

public class UnknownGroupComposer : IServerPacket
{
    private readonly int _groupId;
    private readonly int _habboId;

    public uint MessageId => ServerPacketHeader.UnknownGroupComposer;

    public UnknownGroupComposer(int groupId, int habboId)
    {
        _groupId = groupId;
        _habboId = habboId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_groupId);
        packet.WriteInteger(_habboId);
    }
}
