using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Users;

public class KickbackInfoComposer(ClubKickback info) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.KickbackInfoComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(info.Streak);
        packet.WriteString(info.FirstDate);
        packet.WriteDouble(info.Percentage);
        packet.WriteInteger(info.Missed);
        packet.WriteInteger(info.Rewarded);
        packet.WriteInteger(info.Spent);
        packet.WriteInteger(info.StreakBonus);
        packet.WriteInteger(info.SpendingBonus);
        packet.WriteInteger(info.MinutesUntilPayday);
    }
}
