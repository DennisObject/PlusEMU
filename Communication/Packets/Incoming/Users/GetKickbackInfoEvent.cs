using Plus.Communication.Packets.Outgoing.Users;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Users;

internal class GetKickbackInfoEvent(IClubRewards rewards) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new KickbackInfoComposer(rewards.Kickback(session.GetHabbo())));
        return Task.CompletedTask;
    }
}
