using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.Quests;

internal sealed class GetRewardTracksEvent(IRewardTrackManager rewards) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        rewards.SendTracks(session);
        return Task.CompletedTask;
    }
}
