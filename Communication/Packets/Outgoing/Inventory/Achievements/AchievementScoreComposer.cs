using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Inventory.Achievements;

public class AchievementScoreComposer : IServerPacket
{
    private readonly int _score;
    public uint MessageId => ServerPacketHeader.AchievementScoreComposer;

    public AchievementScoreComposer(int score)
    {
        _score = score;
    }

    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(_score);
}
