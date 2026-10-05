using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

internal class GetGameAchievementsEvent(IAchievementShowcaseService showcase) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        showcase.ShowGameAchievements(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
