using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Recycler;

namespace Plus.Communication.Packets.Incoming.Recycler;

internal sealed class GetRecyclerStatusEvent(IRecyclerService recycler) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        recycler.GetStatus(session);

        return Task.CompletedTask;
    }
}
