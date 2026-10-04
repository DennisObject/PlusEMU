using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Catalog;

internal class SelectClubGiftEvent(IClubRewards rewards) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var claim = rewards.Claim(session.GetHabbo(), packet.ReadString());
        if (claim == null) { session.Send(new PurchaseErrorComposer(0)); return Task.CompletedTask; }
        session.Send(new ClubGiftReceivedComposer(claim.Gift.Item));
        foreach (var item in claim.Items) session.Send(new FurniListNotificationComposer(item.Id, 1));
        session.Send(new FurniListUpdateComposer());
        var gifts = rewards.Gifts(session.GetHabbo());
        session.Send(new ClubGiftsComposer(gifts));
        session.Send(new PickMonthlyClubGiftComposer(gifts.Available));
        return Task.CompletedTask;
    }
}