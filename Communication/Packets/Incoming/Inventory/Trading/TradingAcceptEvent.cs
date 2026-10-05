using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Trading;

namespace Plus.Communication.Packets.Incoming.Inventory.Trading;

internal class TradingAcceptEvent(ITradeRequestService trades) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        trades.Accept(session);
        return Task.CompletedTask;
    }
}