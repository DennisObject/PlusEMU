using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class PurchaseRewardTrackPremiumEvent(IRewardTrackManager rewards) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        rewards.PurchasePremium(session, packet.ReadString());
        return Task.CompletedTask;
    }
}
