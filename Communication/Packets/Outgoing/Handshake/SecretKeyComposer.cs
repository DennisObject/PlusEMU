using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class SecretKeyComposer : IServerPacket
{
    private readonly string _publicKey;
    public uint MessageId => ServerPacketHeader.SecretKeyComposer;

    public SecretKeyComposer(string publicKey)
    {
        _publicKey = publicKey;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteString(_publicKey);
}
