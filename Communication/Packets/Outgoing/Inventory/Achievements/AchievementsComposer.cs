using System.Collections.Immutable;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Achievements;

public class AchievementsComposer(ImmutableArray<AchievementProgressSnapshot> achievements) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.AchievementsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(achievements.Length);

        foreach (var achievement in achievements)
        {
            packet.WriteInteger(achievement.Id); // Unknown (ID?)
            packet.WriteInteger(achievement.TargetLevel); // Target level
            packet.WriteString(achievement.Badge); // Target name/desc/badge
            packet.WriteInteger(1);
            packet.WriteInteger(achievement.Requirement); // Progress req/target
            packet.WriteInteger(achievement.RewardPixels);
            packet.WriteInteger(0); // Type of reward
            packet.WriteInteger(achievement.Progress); // Current progress
            packet.WriteBoolean(achievement.Completed); // Set 100% completed(??)
            packet.WriteString(achievement.Category); // Category
            packet.WriteString(string.Empty);
            packet.WriteInteger(achievement.TotalLevels); // Total amount of levels
            packet.WriteInteger(0);
        }

        packet.WriteString("");
    }
}
