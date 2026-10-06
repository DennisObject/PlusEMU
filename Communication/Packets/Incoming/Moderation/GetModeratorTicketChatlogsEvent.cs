using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTickets)]
internal sealed class GetModeratorTicketChatlogsEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        tickets.SendChatlogs(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
