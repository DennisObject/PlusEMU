using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Trading;

namespace Plus.Communication.Packets.Incoming.Inventory.Trading;

internal class TradingCancelEvent(ITradeRequestService trades) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        trades.Cancel(session);

        return Task.CompletedTask;
    }
}
