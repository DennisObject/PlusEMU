using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public class ModeratorUserRoomVisitsComposer(ModeratorUserRoomVisits history) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorUserRoomVisitsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(history.User.Id);
        packet.WriteString(history.User.Username);
        packet.WriteInteger(history.Visits.Length);

        foreach (var visit in history.Visits)
        {
            packet.WriteUInteger(visit.Room.Id);
            packet.WriteString(visit.Room.Name);
            packet.WriteInteger(visit.EnteredAt.Hour);
            packet.WriteInteger(visit.EnteredAt.Minute);
        }
    }
}
