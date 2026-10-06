using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class FlatAccessDeniedComposer : IServerPacket
{
    private readonly string _username;
    public uint MessageId => ServerPacketHeader.FlatAccessDeniedComposer;

    public FlatAccessDeniedComposer(string username)
    {
        _username = username;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_username);
}
