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
    private const string Cleared = "`auth_ticket` = '', `auth_ticket_expires_at` = NULL, `auth_ticket_exchanged` = 0, `auth_ticket_session` = NULL";

    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public SsoTicketStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.SsoTicketLifetimeSeconds;
    }

    public async Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null)
    {
        var ticket = new IssuedToken(SecureToken.Generate(), Now() + _lifetimeSeconds);
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "UPDATE `users` SET `auth_ticket` = @ticket, `auth_ticket_expires_at` = @expiresAt, `auth_ticket_exchanged` = 0, `auth_ticket_session` = @sessionId WHERE `id` = @userId",
            new { ticket = ticket.Value, expiresAt = ticket.ExpiresAt, sessionId, userId }, scope?.Transaction);
        return ticket;
    }

    public async Task<int?> FindUser(string ticket) => (await FindOwner(ticket))?.UserId;

    public async Task<CredentialOwner?> FindOwner(string ticket)
    {
        if (string.IsNullOrEmpty(ticket))
            return null;
        using var connection = _database.Connection();
        return await connection.QueryFirstOrDefaultAsync<CredentialOwner>(
            $"SELECT `id` AS UserId, `auth_ticket_session` AS SessionId FROM `users` WHERE {LiveTicket} AND `auth_ticket_expires_at` >= @now LIMIT 1",
            new { ticket, now = Now() });
    }

    // The conditional clear is the atomic step: a concurrent login that read the same ticket
    // finds it gone and matches no row.
    public async Task<int?> Consume(string ticket) => (await ClaimFor(ticket, Cleared, ""))?.UserId;

    public Task<CredentialOwner?> Exchange(string ticket) => ClaimFor(ticket, "`auth_ticket_exchanged` = 1", " AND `auth_ticket_exchanged` = 0");

    public async Task Revoke(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync($"UPDATE `users` SET {Cleared} WHERE `id` = @userId", new { userId }, scope?.Transaction);
    }

    public Task RevokeSession(int userId, string sessionId, CredentialScope scope) =>
        scope.Connection.ExecuteAsync($"UPDATE `users` SET {Cleared} WHERE `id` = @userId AND `auth_ticket_session` = @sessionId",
            new { userId, sessionId }, scope.Transaction);

    /// <summary>Applies <paramref name="set"/> to the ticket's row only while it is still the same
    /// live ticket, so exactly one concurrent caller wins.</summary>
    private async Task<CredentialOwner?> ClaimFor(string ticket, string set, string condition)
    {
        var owner = await FindOwner(ticket);
        if (owner == null)
            return null;

        using var connection = _database.Connection();
        var claimed = await connection.ExecuteAsync(
            $"UPDATE `users` SET {set} WHERE `id` = @UserId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now{condition}",
            new { owner.UserId, ticket, now = Now() });
        return claimed == 1 ? owner : null;
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
}
