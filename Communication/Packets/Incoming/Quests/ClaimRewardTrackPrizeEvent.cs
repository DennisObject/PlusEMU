using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class ClaimRewardTrackPrizeEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var trackId = packet.ReadString();
        var prizeId = packet.ReadString();
        return RewardTrackManager.Current?.Claim(session, trackId, prizeId) ?? Task.CompletedTask;
    }
}
