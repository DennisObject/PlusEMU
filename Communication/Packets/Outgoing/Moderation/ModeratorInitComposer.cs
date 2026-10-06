using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public sealed class ModeratorInitComposer(ModeratorInitSnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorInitComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(data.Tickets.Length);

        foreach (var ticket in data.Tickets)
        {
            packet.WriteInteger(ticket.Id);
            packet.WriteInteger((int)ticket.Status);
            packet.WriteInteger(ticket.Type);
            packet.WriteInteger(ticket.Category);
            packet.WriteInteger(ticket.AgeMilliseconds);
            packet.WriteInteger(ticket.Priority);
            packet.WriteInteger(ticket.SenderId);
            packet.WriteInteger(1);
            packet.WriteString(ticket.SenderName);
            packet.WriteInteger(ticket.ReportedId);
            packet.WriteString(ticket.ReportedName);
            packet.WriteInteger(ticket.ModeratorId);
            packet.WriteString(ticket.ModeratorName);
            packet.WriteString(ticket.Issue);
            packet.WriteUInteger(ticket.RoomId);
            packet.WriteInteger(0);
        }

        packet.WriteInteger(data.UserPresets.Length);

        foreach (var preset in data.UserPresets)
        {
            packet.WriteString(preset);
        }

        packet.WriteInteger(0);

        for (var index = 0; index < 7; index++)
        {
            packet.WriteBoolean(true);
        }

        packet.WriteInteger(data.RoomPresets.Length);

        foreach (var preset in data.RoomPresets)
        {
            packet.WriteString(preset);
        }
    }
}
