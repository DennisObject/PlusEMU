using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Recycler;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni;

internal sealed class OpenGiftEvent(IGiftOpeningService gifts, IRecyclerService recycler) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var id = packet.ReadUInt();
        return recycler.TryOpen(session, id) ? Task.CompletedTask : gifts.OpenAsync(session, id);
    }
}
