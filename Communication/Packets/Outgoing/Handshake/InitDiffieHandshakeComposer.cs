using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class InitDiffieHandshakeComposer : IServerPacket
{
    private readonly string _prime;
    private readonly string _generator;
    public uint MessageId => ServerPacketHeader.InitDiffieHandshakeComposer;

    public InitDiffieHandshakeComposer(string prime, string generator)
    {
        _prime = prime;
        _generator = generator;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_prime);
        packet.WriteString(_generator);
    }
}