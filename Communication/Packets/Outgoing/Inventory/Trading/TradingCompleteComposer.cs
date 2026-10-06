using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public class TradingCompleteComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TradingCompleteComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
