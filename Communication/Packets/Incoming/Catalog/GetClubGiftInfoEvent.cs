using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class GetClubGiftInfoEvent(IClubRewards rewards, ICatalogSnapshotService snapshots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new ClubGiftsComposer(snapshots.CaptureClubGifts(rewards.Gifts(session.GetHabbo()))));
        return Task.CompletedTask;
    }
}
