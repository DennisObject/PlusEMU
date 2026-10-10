using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Trading;

namespace Plus.Communication.Packets.Incoming.Inventory.Trading;

internal class TradingOfferItemsEvent(ITradeRequestService trades) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        // AIR class_3171 writes the selected ids after their count. Any other length is rejected.
        var count = packet.ReadInt();

        if (count < 1 || packet.Buffer.Length != (long)count * 4) {
            throw new ArgumentException("The trading offer item list is malformed.");
        }

        var itemIds = new uint[count];

        for (var index = 0; index < count; index++) {
            itemIds[index] = packet.ReadUInt();
        }

        trades.OfferItems(session, itemIds);

        return Task.CompletedTask;
    }
}
