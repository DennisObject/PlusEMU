using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

[Collection(AuthDatabaseFactAttribute.Collection)]
public sealed class AccountStoreDatabaseTests : IDisposable
{
    private readonly List<int> _users = [];
    private readonly AccountStore _store = new(new AuthTestDatabase(), TimeProvider.System, AuthTestConfig.Options(c => c.Registration.Credits = 1234));

    [AuthDatabaseFact]
    public async Task CreatesAUserWithConfiguredDefaultsAndAStatisticsRow()
    {
        var name = AuthTestDatabase.UniqueName("acc");

        var id = Track(await _store.Create(new NewAccount(name, "$argon2id$hash", name + "@example.com", "hd-180-1", "F", "10.1.2.3")));

        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        var row = connection.QuerySingle("SELECT username, password, mail, look, gender, credits, motto, ip_reg, auth_ticket FROM users WHERE id = @id", new { id });
        Assert.Equal(name, (string)row.username);
        Assert.Equal("$argon2id$hash", (string)row.password);
        Assert.Equal("hd-180-1", (string)row.look);
        Assert.Equal("F", (string)row.gender);
        Assert.Equal(1234, (int)row.credits);
        Assert.Equal("Octane", (string)row.motto);
        Assert.Equal("10.1.2.3", (string)row.ip_reg);
        Assert.Equal("", (string)row.auth_ticket);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_statistics WHERE id = @id", new { id }));
    }

    [AuthDatabaseFact]
    public async Task ConcurrentCreatesOfOneNameLeaveOneUserAndNoOrphanStatistics()
    {
        var name = AuthTestDatabase.UniqueName("race");

        var ids = await Task.WhenAll(Enumerable.Range(0, 8).Select(i =>
            Task.Run(() => _store.Create(new NewAccount(i % 2 == 0 ? name : name.ToUpperInvariant(), "h", $"{i}{name}@example.com", "hd-180-1", "M", "10.0.0.1")))));

        var id = Track(Assert.Single(ids, i => i != null));
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM users WHERE username = @name", new { name }));
        Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_statistics WHERE id = @id", new { id }));
    }

    [AuthDatabaseFact]
    public async Task LooksUpCaseInsensitivelyAndReportsExistence()
    {
        var name = AuthTestDatabase.UniqueName("Look");
        var id = Track(AuthTestDatabase.InsertUser(name, "secret", name + "@Example.com"));

        var found = await _store.FindByUsername(name.ToLowerInvariant());

        Assert.Equal(new AccountCredentials(id, name, "secret"), found);
        Assert.Null(await _store.FindByUsername(name + "x"));
        Assert.True(await _store.UsernameExists(name.ToUpperInvariant()));
        Assert.True(await _store.EmailExists(name.ToLowerInvariant() + "@example.com"));
        Assert.False(await _store.EmailExists("x" + name + "@example.com"));
    }

    [AuthDatabaseFact]
    public async Task TheUsernameCollationResolvesAccentVariantsToOneAccount()
    {
        // Why the login lockout counts existing accounts by id rather than by typed name.
        var name = "Den" + Guid.NewGuid().ToString("N")[..9];
        var id = Track(AuthTestDatabase.InsertUser(name, "secret", name + "@example.com"));

        Assert.Equal(id, (await _store.FindByUsername("Dé" + name[2..]))?.Id);
        Assert.Equal(id, (await _store.FindByUsername("DÊ" + name[2..]))?.Id);
    }

    [AuthDatabaseFact]
    public async Task UpgradeOnlyReplacesThePasswordItVerified()
    {
        var id = Track(AuthTestDatabase.InsertUser(AuthTestDatabase.UniqueName("upg"), "Secret"));

        await _store.UpgradePassword(id, "secret", "$argon2id$wrong-case");
        Assert.Equal("Secret", (await _store.FindByUsername(await Name(id)))!.Password);

        await _store.UpgradePassword(id, "Secret", "$argon2id$upgraded");
        Assert.Equal("$argon2id$upgraded", (await _store.FindByUsername(await Name(id)))!.Password);
    }

    private static async Task<string> Name(int id)
    {
        using var connection = new MySqlConnection(AuthTestDatabase.ConnectionString);

        return await connection.QuerySingleAsync<string>("SELECT username FROM users WHERE id = @id", new { id });
    }

    private int Track(int? id)
    {
        Assert.NotNull(id);
        _users.Add(id.Value);

        return id.Value;
    }

    public void Dispose() => AuthTestDatabase.DeleteUsers(_users);
}
