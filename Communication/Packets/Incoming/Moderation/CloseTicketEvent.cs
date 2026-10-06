using Plus.Communication.Attributes;
using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal sealed class CloseTicketEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var result = (SupportTicketResult)packet.ReadInt();
        packet.ReadInt();
        tickets.Close(session, packet.ReadInt(), result);

        return Task.CompletedTask;
    }
}
