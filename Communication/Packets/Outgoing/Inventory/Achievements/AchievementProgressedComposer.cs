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

        packet.WriteInteger(_snapshot.Id); // Unknown (ID?)
        packet.WriteInteger(_snapshot.TargetLevel); // Target level
        packet.WriteString(_snapshot.Badge); // Target name/desc/badge
        packet.WriteInteger(1); // Progress req/target
        packet.WriteInteger(_snapshot.Requirement); // Reward in Pixels
        packet.WriteInteger(_snapshot.RewardPixels); // Reward Ach Score
        packet.WriteInteger(0); // ?
        packet.WriteInteger(_snapshot.Progress); // Current progress
        packet.WriteBoolean(_snapshot.Completed); // Set 100% completed(??)
        packet.WriteString(_snapshot.Category); // Category
        packet.WriteString(string.Empty);
        packet.WriteInteger(_snapshot.TotalLevels); // Total amount of levels
        packet.WriteInteger(0);

    }
}
