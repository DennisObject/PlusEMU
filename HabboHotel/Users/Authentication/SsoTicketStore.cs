using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class SsoTicketStore : ISsoTicketStore
{
    // users.auth_ticket uses a case-insensitive collation; the CAST keeps the comparison exact
    // while the plain equality still lets MySQL use the auth_ticket index.
    private const string LiveTicket = "`auth_ticket` = @ticket AND CAST(`auth_ticket` AS BINARY) = CAST(@ticket AS BINARY)";

    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public SsoTicketStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.SsoTicketLifetimeSeconds;
    }

    public async Task<IssuedToken> Issue(int userId, CredentialScope? scope = null)
    {
        var ticket = new IssuedToken(SecureToken.Generate(), Now() + _lifetimeSeconds);
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "UPDATE `users` SET `auth_ticket` = @ticket, `auth_ticket_expires_at` = @expiresAt, `auth_ticket_exchanged` = 0 WHERE `id` = @userId",
            new { ticket = ticket.Value, expiresAt = ticket.ExpiresAt, userId }, scope?.Transaction);
        return ticket;
    }

    public async Task<int?> FindUser(string ticket)
    {
        if (string.IsNullOrEmpty(ticket))
            return null;
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int?>(
            $"SELECT `id` FROM `users` WHERE {LiveTicket} AND `auth_ticket_expires_at` >= @now LIMIT 1", new { ticket, now = Now() });
    }

    public async Task<int?> Consume(string ticket)
    {
        // The conditional clear is the atomic step: a concurrent login that read the same
        // ticket finds it gone and matches no row.
        return await ClaimFor(ticket, "`auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0", "");
    }

    public async Task<int?> Exchange(string ticket) => await ClaimFor(ticket, "`auth_ticket_exchanged` = 1", " AND `auth_ticket_exchanged` = 0");

    public async Task Revoke(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "UPDATE `users` SET `auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0 WHERE `id` = @userId", new { userId }, scope?.Transaction);
    }

    /// <summary>Applies <paramref name="set"/> to the ticket's row only while it is still the same
    /// live ticket, so exactly one concurrent caller wins.</summary>
    private async Task<int?> ClaimFor(string ticket, string set, string condition)
    {
        var userId = await FindUser(ticket);
        if (userId == null)
            return null;

        using var connection = _database.Connection();
        var claimed = await connection.ExecuteAsync(
            $"UPDATE `users` SET {set} WHERE `id` = @userId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now{condition}",
            new { userId, ticket, now = Now() });
        return claimed == 1 ? userId : null;
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
}
