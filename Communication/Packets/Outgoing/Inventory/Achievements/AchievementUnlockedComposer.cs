using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Achievements;

public class AchievementUnlockedComposer : IServerPacket
{
    private readonly AchievementUnlockSnapshot _snapshot;
    public uint MessageId => ServerPacketHeader.AchievementUnlockedComposer;

    public AchievementUnlockedComposer(AchievementUnlockSnapshot snapshot) => _snapshot = snapshot;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_snapshot.Id); // Achievement type ID.
        packet.WriteInteger(_snapshot.Level); // Achieved level
        packet.WriteInteger(144); // Numeric badge ID (legacy fixed value).
        packet.WriteString(_snapshot.Badge); // Badge code
        packet.WriteInteger(_snapshot.PointReward); // Point reward
        packet.WriteInteger(_snapshot.PixelReward); // Pixel reward
        packet.WriteInteger(0); // Level reward point type.
        packet.WriteInteger(10); // Bonus points (legacy fixed value).
        packet.WriteInteger(21); // Achievement ID (legacy fixed value).
        packet.WriteString(_snapshot.PreviousBadge);
        packet.WriteString(_snapshot.Category);
        packet.WriteBoolean(true);
        packet.WriteInteger(_snapshot.Rarity.OwnerCount); // WIN63 class_3440 ownerCount
        packet.WriteInteger((int)_snapshot.Rarity.Tier); // WIN63 class_3440 badgeRarityId

    }
}
