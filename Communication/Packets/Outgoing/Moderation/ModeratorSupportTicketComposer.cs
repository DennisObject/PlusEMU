using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public sealed class ModeratorSupportTicketComposer(ModeratorTicketSnapshot ticket) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.ModeratorSupportTicketComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(ticket.Id);
        packet.WriteInteger((int)ticket.Status);
        packet.WriteInteger(ticket.Type);
        packet.WriteInteger(ticket.Category);
        packet.WriteInteger(ticket.AgeMilliseconds);
        packet.WriteInteger(ticket.Priority);
        packet.WriteInteger(0);
        packet.WriteInteger(ticket.SenderId);
        packet.WriteString(ticket.SenderName);
        packet.WriteInteger(ticket.ReportedId);
        packet.WriteString(ticket.ReportedName);
        packet.WriteInteger(ticket.ModeratorId);
        packet.WriteString(ticket.ModeratorName);
        packet.WriteString(ticket.Issue);
        packet.WriteUInteger(ticket.RoomId);
        packet.WriteInteger(0);
    }
}
