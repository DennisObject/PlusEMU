using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Dapper;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class CloseTicketEvent : IPacketEvent
{
    private readonly IModerationManager _moderationManager;
    private readonly IGameClientManager _clientManager;
    private readonly IDatabase _database;

    public CloseTicketEvent(IModerationManager moderationManager, IGameClientManager clientManager, IDatabase database)
    {
        _moderationManager = moderationManager;
        _clientManager = clientManager;
        _database = database;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var result = (SupportTicketResult)packet.ReadInt(); // 1 = useless, 2 = abusive, 3 = resolved
        packet.ReadInt(); //junk
        var ticketId = packet.ReadInt();
        if (!_moderationManager.TryGetTicket(ticketId, out var ticket))
            return Task.CompletedTask;
        if (ticket.Moderator.Id != session.GetHabbo().Id)
            return Task.CompletedTask;
        var client = _clientManager.GetClientByUserId(ticket.Sender.Id);
        if (client != null) client.Send(new ModeratorSupportTicketResponseComposer(result));
        if (result == SupportTicketResult.Abusive)
        {
            using var connection = _database.Connection();
            connection.Execute("UPDATE `user_info` SET `cfhs_abusive` = `cfhs_abusive` + 1 WHERE `user_id` = @senderId LIMIT 1",
                new { senderId = ticket.Sender.Id });
        }
        ticket.Answered = true;
        _clientManager.SendPacket(new ModeratorSupportTicketComposer(session.GetHabbo().Id, ticket), PermissionKeys.Definition(PermissionKeys.ModerationTool));
        return Task.CompletedTask;
    }
}