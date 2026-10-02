using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class GetRewardTracksEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        RewardTrackManager.Current?.SendTracks(session);
        return Task.CompletedTask;
    }
}
