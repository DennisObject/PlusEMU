using Dapper;
using Microsoft.Extensions.Options;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class SsoTicketStore : ISsoTicketStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly int _lifetimeSeconds;

    public SsoTicketStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _lifetimeSeconds = options.Value.SsoTicketLifetimeSeconds;
    }

    public async Task<IssuedToken> Issue(int userId)
    {
        var ticket = new IssuedToken(SecureToken.Generate(), Now() + _lifetimeSeconds);
        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE `users` SET `auth_ticket` = @ticket, `auth_ticket_expires_at` = @expiresAt WHERE `id` = @userId",
            new { ticket = ticket.Value, expiresAt = ticket.ExpiresAt, userId });
        return ticket;
    }

    public async Task<int?> FindUser(string ticket)
    {
        if (string.IsNullOrEmpty(ticket))
            return null;
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int?>(
            "SELECT `id` FROM `users` WHERE `auth_ticket` = @ticket AND `auth_ticket_expires_at` >= @now LIMIT 1", new { ticket, now = Now() });
    }

    public async Task<int?> Consume(string ticket)
    {
        var userId = await FindUser(ticket);
        if (userId == null)
            return null;

        // The conditional clear is the atomic step: a concurrent login that read the same
        // ticket finds it gone and matches no row.
        using var connection = _database.Connection();
        var cleared = await connection.ExecuteAsync(
            "UPDATE `users` SET `auth_ticket` = NULL, `auth_ticket_expires_at` = NULL WHERE `id` = @userId AND `auth_ticket` = @ticket",
            new { userId, ticket });
        return cleared == 1 ? userId : null;
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();
}
