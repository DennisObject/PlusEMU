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
    private readonly TimeSpan _lifetime;

    public SsoTicketStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetime = TimeSpan.FromSeconds(options.Value.SsoTicketLifetimeSeconds);

        if (_lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "SSO ticket lifetime must be positive.");
        }
    }

    public Task<IssuedToken> Issue(int userId, string? sessionId = null, CredentialScope? scope = null) =>
        IssueAt(userId, sessionId, CredentialInstant.Capture(_time), scope);

    public async Task<IssuedToken> IssueAt(int userId, string? sessionId, CredentialInstant instant, CredentialScope? scope = null)
    {
        var now = instant.UtcNow;
        var ticket = new IssuedToken(SecureToken.Generate(), now.Add(_lifetime));
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(
            "UPDATE `users` SET `auth_ticket` = @ticket, `auth_ticket_expires_at` = @expiresAt, `auth_ticket_exchanged` = 0, `auth_ticket_session` = @sessionId WHERE `id` = @userId",
            new
            {
                ticket = ticket.Value,
                expiresAt = ticket.ExpiresAt.UtcDateTime,
                sessionId,
                userId
            }, scope?.Transaction);

        return ticket;
    }

    public Task<int?> FindUser(string ticket) => FindUserAt(ticket, CredentialInstant.Capture(_time));

    public async Task<int?> FindUserAt(string ticket, CredentialInstant instant) =>
        (await FindOwnerAt(ticket, instant.UtcNow))?.UserId;

    public async Task<CredentialOwner?> FindOwner(string ticket)
    {
        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        return await FindOwnerAt(ticket, _time.GetUtcNow());
    }

    private async Task<CredentialOwner?> FindOwnerAt(string ticket, DateTimeOffset now)
    {
        using var connection = _database.Connection();

        return await connection.QueryFirstOrDefaultAsync<CredentialOwner>(
            $"SELECT `id` AS UserId, `auth_ticket_session` AS SessionId FROM `users` WHERE {LiveTicket} AND `auth_ticket_expires_at` >= @now LIMIT 1",
            new
            {
                ticket,
                now = now.UtcDateTime
            });
    }

    // The conditional clear is the atomic step: a concurrent login that read the same ticket
    // finds it gone and matches no row.
    public async Task<int?> Consume(string ticket) => (await ClaimFor(ticket, Cleared))?.UserId;

    public Task<CredentialOwner?> Exchange(string ticket) => ExchangeAt(ticket, CredentialInstant.Capture(_time));

    public async Task<CredentialOwner?> ExchangeAt(string ticket, CredentialInstant instant)
    {
        var now = instant.UtcNow;

        if (string.IsNullOrEmpty(ticket) || await FindOwnerAt(ticket, now) is not { } owner)
        {
            return null;
        }

        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        await CredentialGenerations.Lock(connection, transaction, owner.UserId);
        var live = await connection.QuerySingleOrDefaultAsync<CredentialOwner>(
            $"SELECT `id` AS UserId, `auth_ticket_session` AS SessionId FROM `users` WHERE `id` = @UserId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now AND `auth_ticket_exchanged` = 0",
            new
            {
                owner.UserId,
                ticket,
                now = now.UtcDateTime
            }, transaction);

        if (live == null)
        {
            return null;
        }

        var sessionId = live.SessionId;

        if (sessionId == null)
        {
            sessionId = CredentialGenerations.NewSessionId();
            await CredentialGenerations.StartSession(connection, transaction, sessionId, live.UserId, now);
        }

        await connection.ExecuteAsync("UPDATE `users` SET `auth_ticket_exchanged` = 1, `auth_ticket_session` = @sessionId WHERE `id` = @UserId",
            new
            {
                sessionId,
                live.UserId
            }, transaction);
        transaction.Commit();

        return live with
        {
            SessionId = sessionId
        };
    }

    public async Task<CredentialOwner?> Withdraw(int userId, string ticket, CredentialScope scope)
    {
        var now = _time.GetUtcNow();
        var owner = await scope.Connection.QuerySingleOrDefaultAsync<CredentialOwner>(
            $"SELECT `id` AS UserId, `auth_ticket_session` AS SessionId FROM `users` WHERE `id` = @userId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now",
            new
            {
                userId,
                ticket,
                now = now.UtcDateTime
            }, scope.Transaction);

        if (owner != null)
        {
            await scope.Connection.ExecuteAsync($"UPDATE `users` SET {Cleared} WHERE `id` = @userId", new
            {
                userId
            }, scope.Transaction);
        }

        return owner;
    }

    public async Task Revoke(int userId, CredentialScope? scope = null)
    {
        using var owned = scope == null ? _database.Connection() : null;
        await (scope?.Connection ?? owned!).ExecuteAsync(new CommandDefinition($"UPDATE `users` SET {Cleared} WHERE `id` = @userId", new
        {
            userId
        }, scope?.Transaction,
            cancellationToken: scope?.CancellationToken ?? default));
    }

    public Task RevokeSession(int userId, string sessionId, CredentialScope scope) =>
        scope.Connection.ExecuteAsync($"UPDATE `users` SET {Cleared} WHERE `id` = @userId AND `auth_ticket_session` = @sessionId",
            new
            {
                userId,
                sessionId
            }, scope.Transaction);

    /// <summary>Applies <paramref name="set"/> to the ticket's row only while it is still the same
    /// live ticket, so exactly one concurrent caller wins.</summary>
    private async Task<CredentialOwner?> ClaimFor(string ticket, string set)
    {
        var now = _time.GetUtcNow();

        if (string.IsNullOrEmpty(ticket))
        {
            return null;
        }

        var owner = await FindOwnerAt(ticket, now);

        if (owner == null)
        {
            return null;
        }

        using var connection = _database.Connection();
        var claimed = await connection.ExecuteAsync(
            $"UPDATE `users` SET {set} WHERE `id` = @UserId AND {LiveTicket} AND `auth_ticket_expires_at` >= @now",
            new
            {
                owner.UserId,
                ticket,
                now = now.UtcDateTime
            });

        return claimed == 1 ? owner : null;
    }

}
