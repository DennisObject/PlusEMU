using System.Collections.Immutable;
using Dapper;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.Utilities;

namespace Plus.HabboHotel.Moderation;

public sealed record SubmitTicketRequest(string Message, int Category, int ReportedUserId, int Type, ImmutableArray<string> Chats);
public sealed record ModeratorTicketSnapshot(int Id, ModerationTicketStatus Status, int Type, int Category, int AgeMilliseconds,
    int Priority, int SenderId, string SenderName, int ReportedId, string ReportedName, int ModeratorId, string ModeratorName,
    string Issue, uint RoomId, DateTimeOffset CreatedAt);
public sealed record ModeratorInitSnapshot(ImmutableArray<string> UserPresets, ImmutableArray<string> RoomPresets,
    ImmutableArray<ModeratorTicketSnapshot> Tickets);
public sealed record ModeratorTicketChatlogSnapshot(int TicketId, int SenderId, int ReportedId, uint RoomId, string RoomName,
    DateTimeOffset CreatedAt, string ReportedName, ImmutableArray<string> Chats);

public interface IModeratorTicketStore
{
    void RecordSubmission(int userId);
    void RecordAbuse(int userId);
}

public sealed class ModeratorTicketStore(IDatabase database) : IModeratorTicketStore
{
    public void RecordSubmission(int userId)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE user_info SET cfhs=cfhs+1 WHERE user_id=@userId LIMIT 1", new { userId });
    }
    public void RecordAbuse(int userId)
    {
        using var connection = database.Connection();
        connection.Execute("UPDATE user_info SET cfhs_abusive=cfhs_abusive+1 WHERE user_id=@userId LIMIT 1", new { userId });
    }
}

public interface IModeratorTicketService
{
    void SendInitialization(GameClient client);
    void Submit(GameClient client, SubmitTicketRequest request);
    void Pick(GameClient client, int ticketId);
    void Release(GameClient client, IReadOnlyList<int> ticketIds);
    void Close(GameClient client, int ticketId, SupportTicketResult result);
    void DeletePending(GameClient client);
    void SendChatlogs(GameClient client, int ticketId);
}

public sealed class ModeratorTicketService(IModerationManager moderation, IGameClientManager clients,
    IModeratorUserLookup users, IModeratorTicketStore store, TimeProvider clock, IRoomDataLoader rooms) : IModeratorTicketService
{
    private readonly object _submissionLock = new();

    public static ModeratorTicketSnapshot Capture(ModerationTicket ticket, int viewerId, DateTimeOffset now) =>
        new(ticket.Id, ticket.GetStatus(viewerId), ticket.Type, ticket.Category,
            Convert.ToInt32(Math.Clamp((now - ticket.CreatedAt).TotalMilliseconds, 0, int.MaxValue)), ticket.Priority,
            ticket.Sender.Id, ticket.Sender.Username, ticket.Reported?.Id ?? 0, ticket.Reported?.Username ?? "",
            ticket.Moderator?.Id ?? 0, ticket.Moderator?.Username ?? "", ticket.Issue, ticket.Room?.Id ?? 0, ticket.CreatedAt);

    public void SendInitialization(GameClient client)
    {
        var now = clock.GetUtcNow();
        // The initial packet historically calculates each tab relative to its ticket id.
        client.Send(new ModeratorInitComposer(new(moderation.UserMessagePresets.ToImmutableArray(),
            moderation.RoomMessagePresets.ToImmutableArray(), moderation.GetTickets.Select(ticket => Capture(ticket, ticket.Id, now)).ToImmutableArray())));
    }

    public void Submit(GameClient client, SubmitTicketRequest request)
    {
        var sender = client.GetHabbo();
        lock (_submissionLock)
        {
            if (moderation.UserHasTickets(sender.Id))
            {
                var pending = moderation.GetTicketBySenderId(sender.Id);
                if (pending != null)
                {
                    client.Send(new CallForHelpPendingCallsComposer(Capture(pending, sender.Id, clock.GetUtcNow())));
                    return;
                }
            }
            var reported = users.GetById(request.ReportedUserId);
            if (reported == null) return;
            var ticket = new ModerationTicket(1, request.Type, request.Category, clock.GetUtcNow(), 1, sender, reported,
                StringCharFilter.Escape(request.Message.Trim()), sender.CurrentRoom?.Data, request.Chats.ToList());
            store.RecordSubmission(sender.Id);
            if (!moderation.TryAddTicket(ticket)) return;
            clients.ModAlert("A new support ticket has been submitted!");
            Broadcast(ticket, sender.Id);
        }
    }

    public void Pick(GameClient client, int ticketId)
    {
        if (!moderation.TryGetTicket(ticketId, out var ticket)) return;
        ticket.Moderator = client.GetHabbo();
        Broadcast(ticket, client.GetHabbo().Id);
    }

    public void Release(GameClient client, IReadOnlyList<int> ticketIds)
    {
        foreach (var id in ticketIds)
        {
            if (!moderation.TryGetTicket(id, out var ticket)) continue;
            ticket.Moderator = null;
            Broadcast(ticket, client.GetHabbo().Id);
        }
    }

    public void Close(GameClient client, int ticketId, SupportTicketResult result)
    {
        if (!moderation.TryGetTicket(ticketId, out var ticket) || ticket.Moderator?.Id != client.GetHabbo().Id) return;
        if (result == SupportTicketResult.Abusive) store.RecordAbuse(ticket.Sender.Id);
        clients.GetClientByUserId(ticket.Sender.Id)?.Send(new ModeratorSupportTicketResponseComposer(result));
        ticket.Answered = true;
        Broadcast(ticket, client.GetHabbo().Id);
    }

    public void DeletePending(GameClient client)
    {
        if (!moderation.UserHasTickets(client.GetHabbo().Id)) return;
        var ticket = moderation.GetTicketBySenderId(client.GetHabbo().Id);
        if (ticket == null) return;
        ticket.Answered = true;
        Broadcast(ticket, client.GetHabbo().Id);
    }

    public void SendChatlogs(GameClient client, int ticketId)
    {
        if (!moderation.TryGetTicket(ticketId, out var ticket) || ticket.Room == null ||
            !rooms.TryGetData(ticket.Room.Id, out var room)) return;
        client.Send(new ModeratorTicketChatlogComposer(new(ticket.Id, ticket.Sender.Id, ticket.Reported?.Id ?? 0,
            room.Id, room.Name, ticket.CreatedAt, ticket.Reported?.Username ?? "No username", ticket.ReportedChats.ToImmutableArray())));
    }

    private void Broadcast(ModerationTicket ticket, int viewerId) => clients.SendPacket(
        new ModeratorSupportTicketComposer(Capture(ticket, viewerId, clock.GetUtcNow())), PermissionKeys.Definition(PermissionKeys.ModerationTool));
}
