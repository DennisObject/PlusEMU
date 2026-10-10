using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class SsoTicketStore : ISsoTicketStore
{
    // users.auth_ticket uses a case-insensitive collation; the CAST keeps the comparison exact
    // while the plain equality still lets MySQL use the auth_ticket index.
    private const string LiveTicket = "`auth_ticket` = @ticket AND CAST(`auth_ticket` AS BINARY) = CAST(@ticket AS BINARY)";
    private const string Cleared = "`auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0, `auth_ticket_session` = NULL";

    private readonly IDatabase _database;
    private readonly TimeProvider _time;

    public SsoTicketStore(IDatabase database, TimeProvider time)
    {
        _database = database;
        _time = time;
    }

    // The conditional clear is the atomic step: a concurrent login that read the same ticket
    // finds it gone and matches no row.
    public async Task<int?> Consume(string ticket) => (await ClaimFor(ticket, Cleared))?.UserId;

    public async Task Revoke(int userId, CancellationToken cancellationToken = default)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync(new CommandDefinition($"UPDATE `users` SET {Cleared} WHERE `id` = @userId", new { userId },
            cancellationToken: cancellationToken));
    }

    private async Task<TicketOwner?> FindOwnerAt(string ticket, DateTimeOffset now)
    {
        using var connection = _database.Connection();

        return await connection.QueryFirstOrDefaultAsync<TicketOwner>(
            $"SELECT `id` AS UserId FROM `users` WHERE {LiveTicket} AND `auth_ticket_expires_at` >= @now LIMIT 1",
            new { ticket, now = now.UtcDateTime });
    }

    /// <summary>Applies <paramref name="set"/> to the ticket's row only while it is still the same
    /// live ticket, so exactly one concurrent caller wins.</summary>
    private async Task<TicketOwner?> ClaimFor(string ticket, string set)
    {
        var now = _time.GetUtcNow();

        if (string.IsNullOrEmpty(ticket)) {
            return null;
        }

        var owner = await FindOwnerAt(ticket, now);

        if (owner == null) {
            return null;
        }

        using var connection = _database.Connection();
        var claimed = await connection.ExecuteAsync(
            $"UPDATE `users` SET {set} WHERE `id` = @UserId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now",
            new { owner.UserId, ticket, now = now.UtcDateTime });

        return claimed == 1 ? owner : null;
    }

    private sealed record TicketOwner(int UserId);
}
