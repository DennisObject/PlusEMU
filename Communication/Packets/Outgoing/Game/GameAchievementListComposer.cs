using System.Collections.Immutable;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Game;

public class GameAchievementListComposer(int gameId, ImmutableArray<AchievementProgressSnapshot> achievements) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GameAchievementListComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(gameId);
        packet.WriteInteger(achievements.Length);
        foreach (var achievement in achievements)
        {
            packet.WriteInteger(achievement.Id); // ach id
            packet.WriteInteger(achievement.TargetLevel); // target level
            packet.WriteString(achievement.Badge); // badge
            packet.WriteInteger(achievement.Requirement); // requirement
            packet.WriteInteger(achievement.Requirement); // requirement
            packet.WriteInteger(achievement.RewardPixels); // pixels
            packet.WriteInteger(0); // ach score
            packet.WriteInteger(achievement.Progress); // Current progress
            packet.WriteBoolean(achievement.Completed); // Set 100% completed(??)
            packet.WriteString(achievement.Category);
            packet.WriteString("basejump");
            packet.WriteInteger(0); // total levels
            packet.WriteInteger(0);
        }
        packet.WriteString("");
    }
}
