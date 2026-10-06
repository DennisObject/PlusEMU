using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public class PurchaseErrorComposer : IServerPacket
{
    private readonly PurchaseError _errorCode;
    public uint MessageId => ServerPacketHeader.PurchaseErrorComposer;

    public PurchaseErrorComposer(PurchaseError errorCode)
    {
        _errorCode = errorCode;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger((int)_errorCode);
}
