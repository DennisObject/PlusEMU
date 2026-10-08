using System.Data;
using Dapper;

namespace Plus.HabboHotel.Users;

// Habbo's activity point types (the points types catalog offers and the purse use). Any type >= 0 is valid.
public static class ActivityPointType
{
    public const int Duckets = 0;
    public const int Diamonds = 5;
    public const int Gotw = 103;

    public static bool IsValid(int type) => type >= 0;
}

// A user's activity point balances by type; a type the user never had is 0. Credits are not activity points.
public sealed class UserCurrencies
{
    private readonly Dictionary<int, int> _amounts = new();

    public int this[int type]
    {
        get
        {
            lock (_amounts) {
                return _amounts.GetValueOrDefault(type);
            }
        }
        set
        {
            if (!ActivityPointType.IsValid(type)) {
                throw new ArgumentOutOfRangeException(nameof(type));
            }

            lock (_amounts) {
                _amounts[type] = value;
            }
        }
    }

    // Every type the user holds a row for, plus the given types, ordered by type.
    public IReadOnlyList<KeyValuePair<int, int>> Snapshot(params int[] alwaysInclude)
    {
        lock (_amounts) {
            return _amounts.Keys.Concat(alwaysInclude).Distinct().Order()
                .Select(type => new KeyValuePair<int, int>(type, _amounts.GetValueOrDefault(type))).ToArray();
        }
    }

    public void Load(IEnumerable<KeyValuePair<int, int>> amounts)
    {
        lock (_amounts) {
            _amounts.Clear();

            foreach (var (type, amount) in amounts) {
                _amounts[type] = amount;
            }
        }
    }
}

// user_currencies: one row per user and activity point type; a missing row is a balance of 0.
public static class UserCurrencyStore
{
    public static IReadOnlyList<KeyValuePair<int, int>> Load(IDbConnection connection, int userId, IDbTransaction? transaction = null) =>
        connection.Query<(int Type, int Amount)>("SELECT type, amount FROM user_currencies WHERE user_id = @userId ORDER BY type", new { userId }, transaction)
            .Select(row => new KeyValuePair<int, int>(row.Type, row.Amount)).ToArray();

    public static int Get(IDbConnection connection, int userId, int type, IDbTransaction? transaction = null, bool forUpdate = false) =>
        connection.ExecuteScalar<int?>("SELECT amount FROM user_currencies WHERE user_id = @userId AND type = @type" + (forUpdate ? " FOR UPDATE" : ""),
            new { userId, type }, transaction) ?? 0;

    public static void Set(IDbConnection connection, int userId, int type, int amount, IDbTransaction? transaction = null) =>
        SetMany(connection, userId, [new(type, amount)], transaction);

    public static void SetMany(IDbConnection connection, int userId, IEnumerable<KeyValuePair<int, int>> amounts, IDbTransaction? transaction = null)
    {
        var rows = amounts.Select(pair => new { userId, type = pair.Key, amount = pair.Value }).ToArray();

        if (rows.Any(row => !ActivityPointType.IsValid(row.type))) {
            throw new ArgumentOutOfRangeException(nameof(amounts));
        }

        if (rows.Length > 0) {
            connection.Execute("INSERT INTO user_currencies (user_id, type, amount) VALUES (@userId, @type, @amount) ON DUPLICATE KEY UPDATE amount = VALUES(amount)",
                rows, transaction);
        }
    }

    // Adds delta (which may be negative) to a balance in the database, creating the row at 0 first.
    public static void Add(IDbConnection connection, int userId, int type, int delta, IDbTransaction? transaction = null)
    {
        if (!ActivityPointType.IsValid(type)) {
            throw new ArgumentOutOfRangeException(nameof(type));
        }

        if (delta != 0) {
            connection.Execute("INSERT INTO user_currencies (user_id, type, amount) VALUES (@userId, @type, @delta) ON DUPLICATE KEY UPDATE amount = amount + VALUES(amount)",
                new { userId, type, delta }, transaction);
        }
    }
}
