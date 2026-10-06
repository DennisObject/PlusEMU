using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Users;

public interface ITradingLockService
{
    DateTimeOffset Set(int userId, TimeSpan duration);
    void Clear(int userId);
    bool IsLocked(Habbo habbo);
}

public sealed class TradingLockService(IDatabase database, IGameClientManager clients, IAccountSessionGate accounts, TimeProvider clock) : ITradingLockService
{
    public DateTimeOffset Set(int userId, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // DATETIME has second precision. Keep the live state identical to the stored expiry.
        var expiry = clock.GetUtcNow().Add(duration).ToUniversalTime();
        var until = new DateTimeOffset(expiry.Ticks - expiry.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        using var account = accounts.Enter(userId);
        using var connection = database.Connection();
        connection.Execute("INSERT INTO `user_info` (`user_id`, `trading_locked`, `trading_locks_count`) VALUES (@userId, @until, 1) " +
            "ON DUPLICATE KEY UPDATE `trading_locked` = @until, `trading_locks_count` = `trading_locks_count` + 1",
            new { userId, until = until.UtcDateTime });

        if (clients.GetClientByUserId(userId)?.GetHabbo() is { } habbo) {
            habbo.TradingLockExpiresAt = until;
        }

        return until;
    }

    public void Clear(int userId)
    {
        using var account = accounts.Enter(userId);
        using var connection = database.Connection();
        connection.Execute("UPDATE `user_info` SET `trading_locked` = NULL WHERE `user_id` = @userId", new { userId });

        if (clients.GetClientByUserId(userId)?.GetHabbo() is { } habbo) {
            habbo.TradingLockExpiresAt = null;
        }
    }

    public bool IsLocked(Habbo habbo)
    {
        using var account = accounts.Enter(habbo.Id);

        if (habbo.TradingLockExpiresAt is not { } expiresAt) {
            return false;
        }

        var now = clock.GetUtcNow();

        if (expiresAt > now) {
            return true;
        }

        using var connection = database.Connection();
        // A concurrent sanction in another process must survive expiration of this session's old lock.
        connection.Execute("UPDATE `user_info` SET `trading_locked` = NULL WHERE `user_id` = @userId AND `trading_locked` <= @now",
            new { userId = habbo.Id, now = now.UtcDateTime });
        habbo.TradingLockExpiresAt = connection.QuerySingleOrDefault<DateTimeOffset?>(
            "SELECT `trading_locked` FROM `user_info` WHERE `user_id` = @userId", new { userId = habbo.Id });

        return habbo.TradingLockExpiresAt > now;
    }
}
