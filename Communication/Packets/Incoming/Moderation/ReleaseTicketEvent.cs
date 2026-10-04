using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class ReleaseTicketEvent : IPacketEvent
{
    public readonly IModerationManager _moderationManager;
    public readonly IGameClientManager _clientManager;

    public ReleaseTicketEvent(IModerationManager moderationManager, IGameClientManager clientManager)
    {
        _moderationManager = moderationManager;
        _clientManager = clientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var amount = packet.ReadInt();
        for (var i = 0; i < amount; i++)
        {
            if (!_moderationManager.TryGetTicket(packet.ReadInt(), out var ticket))
                continue;
            ticket.Moderator = null;
            _clientManager.SendPacket(new ModeratorSupportTicketComposer(session.GetHabbo().Id, ticket), "mod_tool");
        }
        return Task.CompletedTask;
    }
}