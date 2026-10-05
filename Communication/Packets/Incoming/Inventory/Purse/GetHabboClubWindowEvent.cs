using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Inventory.Purse;

internal sealed class GetHabboClubWindowEvent(IClubOfferSnapshotService offers) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        offers.ShowOffers(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
