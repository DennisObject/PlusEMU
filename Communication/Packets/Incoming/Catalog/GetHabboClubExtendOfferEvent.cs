using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal sealed class GetHabboClubExtendOfferEvent(IClubOfferSnapshotService offers) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        offers.ShowExtension(session);

        return Task.CompletedTask;
    }
}
