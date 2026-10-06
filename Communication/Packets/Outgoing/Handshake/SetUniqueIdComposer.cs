using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class SetUniqueIdComposer : IServerPacket
{
    private readonly string _id;
    public uint MessageId => ServerPacketHeader.SetUniqueIdComposer;

    public SetUniqueIdComposer(string id)
    {
        _id = id;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_id);
}
