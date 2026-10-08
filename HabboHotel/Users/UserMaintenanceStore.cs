using System.Data;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Users.Grants;

namespace Plus.HabboHotel.Users;

public interface IUserMaintenanceStore
{
    int? ReadCurrency(int userId, UserCurrency currency);
    bool TryWriteCurrency(int userId, UserCurrency currency, int value);

    /// <summary>Adds a signed delta to an offline balance in one locked transaction; the error is a <see cref="GrantOutcome"/> code.</summary>
    (string? Error, int Balance) ChangeCurrency(int userId, UserCurrency currency, long delta);
    string? ReadMotto(int userId);
}

/// <summary>Parameterized maintenance SQL; credits live in users, activity points in user_currencies by a closed enum switch.</summary>
public sealed class UserMaintenanceStore(IDatabase database) : IUserMaintenanceStore
{
    public int? ReadCurrency(int userId, UserCurrency currency)
    {
        using var connection = database.Connection();

        if (currency == UserCurrency.Credits) {
            return connection.QuerySingleOrDefault<int?>("SELECT credits FROM users WHERE id = @userId", new { userId });
        }

        return connection.QuerySingleOrDefault<int?>("SELECT COALESCE(c.amount, 0) FROM users u LEFT JOIN user_currencies c ON c.user_id = u.id AND c.type = @type WHERE u.id = @userId",
            new { userId, type = PointsType(currency) });
    }

    // Existence is the row contract: the row is locked, must exist exactly once, and an identical value still succeeds.
    public bool TryWriteCurrency(int userId, UserCurrency currency, int value)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.Query<int>("SELECT id FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction).Count() != 1) {
            return false;
        }

        if (currency == UserCurrency.Credits) {
            connection.Execute("UPDATE users SET credits = @value WHERE id = @userId LIMIT 1", new { userId, value }, transaction);
        }
        else {
            UserCurrencyStore.Set(connection, userId, PointsType(currency), value, transaction);
        }

        transaction.Commit();

        return true;
    }

    public (string? Error, int Balance) ChangeCurrency(int userId, UserCurrency currency, long delta)
    {
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();

        if (connection.QuerySingleOrDefault<int?>("SELECT credits FROM users WHERE id = @userId FOR UPDATE", new { userId }, transaction) is not { } credits) {
            return (GrantOutcome.UserNotFound, 0);
        }

        var balance = (currency == UserCurrency.Credits ? credits : UserCurrencyStore.Get(connection, userId, PointsType(currency), transaction, forUpdate: true)) + delta;

        if (GrantOutcome.CheckBalance(balance) is { } error) {
            return (error, 0);
        }

        if (currency == UserCurrency.Credits) {
            connection.Execute("UPDATE users SET credits = @balance WHERE id = @userId LIMIT 1", new { userId, balance }, transaction);
        }
        else {
            UserCurrencyStore.Set(connection, userId, PointsType(currency), (int)balance, transaction);
        }

        transaction.Commit();

        return (null, (int)balance);
    }

    public string? ReadMotto(int userId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<string?>("SELECT motto FROM users WHERE id = @userId", new { userId });
    }

    private static int PointsType(UserCurrency currency) => currency switch
    {
        UserCurrency.Duckets => ActivityPointType.Duckets,
        UserCurrency.Diamonds => ActivityPointType.Diamonds,
        UserCurrency.Gotw => ActivityPointType.Gotw,
        _ => throw new ArgumentOutOfRangeException(nameof(currency)),
    };
}
