using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Recycler;

namespace Plus.Communication.Packets.Incoming.Recycler;

internal sealed class RecyclerRecycleEvent(IRecyclerService recycler) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var count = packet.ReadInt();
        if (count is < 1 or > 12 || count > (packet.Stream.Length - packet.Stream.Position) / sizeof(int)) {
            recycler.Recycle(session, []);
            return Task.CompletedTask;
        }
        var ids = new uint[count];
        for (var index = 0; index < count; index++) {
            ids[index] = packet.ReadUInt();
        }
        recycler.Recycle(session, ids);
        return Task.CompletedTask;
    }
}
