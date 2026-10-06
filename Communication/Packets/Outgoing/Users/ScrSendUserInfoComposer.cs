using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Users;

public class ScrSendUserInfoComposer(ClubStatusSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ScrSendUserInfoComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString("habbo_club");
        packet.WriteInteger(snapshot.DaysLeft);
        packet.WriteInteger(snapshot.PeriodsElapsed);
        packet.WriteInteger(snapshot.MonthsAhead);
        packet.WriteInteger(snapshot.ResponseType);
        packet.WriteBoolean(snapshot.HasEverStarted);
        // HC branding in the purse is level 1; rights still carry level 2 for all merged benefits.
        packet.WriteBoolean(false);
        packet.WriteInteger(snapshot.ElapsedDays);
        packet.WriteInteger(0);
        packet.WriteInteger(snapshot.MinutesLeft);
        packet.WriteInteger(snapshot.MinutesSinceModified);
    }
}
