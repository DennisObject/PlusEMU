using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class PickTicketEvent : IPacketEvent
{
    public readonly IModerationManager _moderationManager;
    public readonly IGameClientManager _clientManager;

    public PickTicketEvent(IModerationManager moderationManager, IGameClientManager clientManager)
    {
        _moderationManager = moderationManager;
        _clientManager = clientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); //Junk
        var ticketId = packet.ReadInt();
        if (!_moderationManager.TryGetTicket(ticketId, out var ticket))
            return Task.CompletedTask;
        ticket.Moderator = session.GetHabbo();
        _clientManager.SendPacket(new ModeratorSupportTicketComposer(session.GetHabbo().Id, ticket), PermissionKeys.Definition(PermissionKeys.ModerationTool));
        return Task.CompletedTask;
    }
}