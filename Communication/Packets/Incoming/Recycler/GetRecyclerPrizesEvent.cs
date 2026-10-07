using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Recycler;

namespace Plus.Communication.Packets.Incoming.Recycler;

internal sealed class GetRecyclerPrizesEvent(IRecyclerService recycler) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        recycler.GetPrizes(session);
        return Task.CompletedTask;
    }
}
