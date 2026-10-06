using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Trading;

public class TradingFinishComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.TradingFinishComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Empty Body
    }
}
