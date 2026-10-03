using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Plus.Communication.Http;
using Plus.Database;

namespace Plus.HabboHotel.Users.Authentication;

public class AccountStore : IAccountStore
{
    private readonly IDatabase _database;
    private readonly TimeProvider _time;
    private readonly RegistrationDefaults _defaults;

    public AccountStore(IDatabase database, TimeProvider time, IOptions<AuthApiConfiguration> options)
    {
        _database = database;
        _time = time;
        _defaults = options.Value.Registration;
    }

    public async Task<AccountCredentials?> FindByUsername(string username)
    {
        using var connection = _database.Connection();
        return await connection.QuerySingleOrDefaultAsync<AccountCredentials>(
            "SELECT `id` AS Id, `username` AS Username, `password` AS Password FROM `users` WHERE `username` = @username LIMIT 1", new { username });
    }

    public async Task UpgradePassword(int userId, string current, string replacement)
    {
        using var connection = _database.Connection();
        await connection.ExecuteAsync("UPDATE `users` SET `password` = @replacement WHERE `id` = @userId AND BINARY `password` = BINARY @current",
            new { userId, current, replacement });
    }

    public async Task<bool> UsernameExists(string username)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `username` = @username", new { username }) != 0;
    }

    public async Task<bool> EmailExists(string email)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>("SELECT COUNT(0) FROM `users` WHERE `mail` = @email", new { email }) != 0;
    }

    public async Task<int?> Create(NewAccount account)
    {
        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var userId = await connection.ExecuteScalarAsync<int>(
                "INSERT INTO `users` (`username`, `password`, `mail`, `auth_ticket`, `rank`, `look`, `gender`, `motto`, `credits`, `activity_points`, `vip`, " +
                "`account_created`, `last_online`, `home_room`, `ip_reg`, `ip_last`, `is_ambassador`, `bubble_id`) " +
                "VALUES (@Username, @PasswordHash, @Email, '', @Rank, @Look, @Gender, @Motto, @Credits, @ActivityPoints, @Vip, " +
                "@Now, @Now, @HomeRoom, @Address, @Address, 0, 0); SELECT LAST_INSERT_ID();",
                new
                {
                    account.Username, account.PasswordHash, account.Email, account.Look, account.Gender, account.Address,
                    _defaults.Rank, _defaults.Motto, _defaults.Credits, _defaults.ActivityPoints, _defaults.HomeRoom,
                    Vip = _defaults.Vip ? "1" : "0",
                    Now = _time.GetUtcNow().ToUnixTimeSeconds()
                }, transaction);
            await connection.ExecuteAsync("INSERT INTO `user_statistics` (`id`) VALUES (@userId)", new { userId }, transaction);
            transaction.Commit();
            return userId;
        }
        catch (MySqlException e) when (e.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            transaction.Rollback();
            return null;
        }
    }
}
