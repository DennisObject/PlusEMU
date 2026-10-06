using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Inventory.Purse;

internal class GetCreditsInfoEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
        session.Send(new ActivityPointsComposer(session.GetHabbo().Duckets, session.GetHabbo().Diamonds, session.GetHabbo().GotwPoints));

        return Task.CompletedTask;
    }
}
