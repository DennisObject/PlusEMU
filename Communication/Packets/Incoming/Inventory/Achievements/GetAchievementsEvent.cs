using Plus.Communication.Packets.Outgoing.Inventory.Achievements;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Inventory.Achievements;

internal class GetAchievementsEvent(IAchievementManager achievementManager, IAchievementSnapshotService snapshots) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        session.Send(new AchievementsComposer(snapshots.Capture(session.GetHabbo(), achievementManager.Achievements.Values)));
        return Task.CompletedTask;
    }
}
