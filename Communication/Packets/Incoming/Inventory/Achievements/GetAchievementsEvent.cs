using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Inventory.Achievements;

internal class GetAchievementsEvent(IAchievementShowcaseService showcase) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        showcase.ShowAchievements(session);
        return Task.CompletedTask;
    }
}
