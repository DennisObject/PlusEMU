using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.GameClients;
using Plus.Communication.Packets.Outgoing.Habbicons;

namespace Plus.HabboHotel.Habbicons;

public static class HabbiconMessages
{
    public static void SendSnapshot(GameClient session, HabbiconSnapshot snapshot)
    {
        session.Send(new UserHabbiconsComposer(snapshot));

        if (snapshot.Unseen.Count > 0) {
            session.Send(new HabbiconUnseenComposer(snapshot.Unseen));
        }
    }

    public static void Publish(GameClient session, HabbiconChange change)
    {
        foreach (var item in change.Changed) {
            session.Send(new UserHabbiconStatusChangedComposer(item.Id, item.State));
        }

        SendSnapshot(session, change.Snapshot);

        if (change.Balances != null) {
            var habbo = session.GetHabbo();

            lock (habbo.WalletSync) {
                session.Send(new CreditBalanceComposer(habbo.Credits));

                if (change.Balances.ChargedType is { } type) {
                    session.Send(new HabboActivityPointNotificationComposer(habbo.Currencies[type], 0, type));
                }
            }
        }
    }
}
