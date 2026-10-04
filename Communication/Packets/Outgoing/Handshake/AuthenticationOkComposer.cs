using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class AuthenticationOkComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AuthenticationOkComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}