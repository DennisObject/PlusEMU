using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Trading;

namespace Plus.Communication.Packets.Incoming.Inventory.Trading;

internal class InitTradeEvent(ITradeRequestService trades) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        trades.Start(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}