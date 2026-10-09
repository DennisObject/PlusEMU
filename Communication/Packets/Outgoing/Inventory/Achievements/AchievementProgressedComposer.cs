using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Achievements;

public class AchievementProgressedComposer : IServerPacket
{
    private readonly AchievementProgressSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.AchievementProgressedComposer;

    public AchievementProgressedComposer(AchievementProgressSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {

        packet.WriteInteger(_snapshot.Id); // Achievement ID
        packet.WriteInteger(_snapshot.TargetLevel); // Target level
        packet.WriteString(_snapshot.Badge); // Badge code
        packet.WriteInteger(1); // Score at the start of the level (legacy fixed value).
        packet.WriteInteger(_snapshot.Requirement); // Score required to complete this level.
        packet.WriteInteger(_snapshot.RewardPixels); // Level reward points.
        packet.WriteInteger(0); // Level reward point type.
        packet.WriteInteger(_snapshot.Progress); // Current progress
        packet.WriteBoolean(_snapshot.Completed); // Final achievement level completed.
        packet.WriteString(_snapshot.Category); // Category
        packet.WriteString(string.Empty);
        packet.WriteInteger(_snapshot.TotalLevels); // Total amount of levels
        packet.WriteInteger(0);

    }
}
