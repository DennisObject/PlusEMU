using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public class GenericErrorComposer : IServerPacket
{
    private readonly GenericError _errorId;
    public uint MessageId => ServerPacketHeader.GenericErrorComposer;

    public GenericErrorComposer(GenericError errorId)
    {
        _errorId = errorId;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger((int)_errorId);
    }
}
