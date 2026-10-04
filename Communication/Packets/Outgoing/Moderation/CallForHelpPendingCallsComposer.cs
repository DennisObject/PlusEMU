using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Outgoing.Moderation;

public sealed class CallForHelpPendingCallsComposer(ModeratorTicketSnapshot ticket) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CallForHelpPendingCallsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteString(ticket.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        packet.WriteString(ticket.CreatedAt.UtcDateTime.ToShortTimeString());
        packet.WriteString(ticket.Issue);
    }
}
