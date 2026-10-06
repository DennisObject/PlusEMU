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
        packet.WriteInteger(_snapshot.Id); // Achievement ID
        packet.WriteInteger(_snapshot.Level); // Achieved level
        packet.WriteInteger(144); // Unknown. Random useless number.
        packet.WriteString(_snapshot.Badge); // Achieved name
        packet.WriteInteger(_snapshot.PointReward); // Point reward
        packet.WriteInteger(_snapshot.PixelReward); // Pixel reward
        packet.WriteInteger(0); // Unknown.
        packet.WriteInteger(10); // Unknown.
        packet.WriteInteger(21); // Unknown. (Extra reward?)
        packet.WriteString(_snapshot.PreviousBadge);
        packet.WriteString(_snapshot.Category);
        packet.WriteBoolean(true);

    }
}
