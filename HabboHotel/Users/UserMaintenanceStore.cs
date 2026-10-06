using System.Data;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Users;

public interface IUserMaintenanceStore
{
    int? ReadCurrency(int userId, UserCurrency currency);
    bool TryWriteCurrency(int userId, UserCurrency currency, int value);
    string? ReadMotto(int userId);
}

/// <summary>Parameterized maintenance SQL; the balance column comes from a closed enum switch, never from input.</summary>
public sealed class UserMaintenanceStore(IDatabase database) : IUserMaintenanceStore
{
    public int? ReadCurrency(int userId, UserCurrency currency)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<int?>($"SELECT {Column(currency)} FROM users WHERE id = @userId", new { userId });
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

        connection.Execute($"UPDATE users SET {Column(currency)} = @value WHERE id = @userId LIMIT 1", new { userId, value }, transaction);
        transaction.Commit();

        return true;
    }

    public string? ReadMotto(int userId)
    {
        using var connection = database.Connection();

        return connection.QuerySingleOrDefault<string?>("SELECT motto FROM users WHERE id = @userId", new { userId });
    }

    private static string Column(UserCurrency currency) => currency switch
    {
        UserCurrency.Credits => "credits",
        UserCurrency.Duckets => "activity_points",
        UserCurrency.Diamonds => "vip_points",
        UserCurrency.Gotw => "gotw_points",
        _ => throw new ArgumentOutOfRangeException(nameof(currency)),
    };
}
