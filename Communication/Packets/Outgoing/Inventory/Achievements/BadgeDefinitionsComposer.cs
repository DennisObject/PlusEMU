using System.Collections.Immutable;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Achievements;

public class BadgeDefinitionsComposer : IServerPacket
{
    private readonly ImmutableArray<AchievementDefinitionSnapshot> _achievements;
    public uint MessageId => ServerPacketHeader.BadgeDefinitionsComposer;

    public BadgeDefinitionsComposer(ImmutableArray<AchievementDefinitionSnapshot> achievements)
    {
        _achievements = achievements;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_achievements.Length);

        foreach (var achievement in _achievements)
        {
            packet.WriteString(achievement.Name);
            packet.WriteInteger(achievement.Levels.Length);

            foreach (var level in achievement.Levels)
            {
                packet.WriteInteger(level.Level);
                packet.WriteInteger(level.Requirement);
            }
        }
    }
}
