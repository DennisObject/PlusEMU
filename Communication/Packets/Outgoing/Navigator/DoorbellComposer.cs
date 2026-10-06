using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class DoorbellComposer : IServerPacket
{
    private readonly string _username;

    public uint MessageId => ServerPacketHeader.DoorbellComposer;

    public DoorbellComposer(string username)
    {
        _username = username;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_username);
}
