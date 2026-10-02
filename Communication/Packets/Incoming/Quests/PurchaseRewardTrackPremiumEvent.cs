using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class PurchaseRewardTrackPremiumEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        RewardTrackManager.Current?.PurchasePremium(session, packet.ReadString());
        return Task.CompletedTask;
    }
}
