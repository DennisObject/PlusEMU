using System.Data;
using Dapper;
using Microsoft.Extensions.Options;
using MySqlConnector;
using Plus.Communication.Http;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.Authentication;
using Xunit;

namespace Plus.Tests;

/// <summary>
/// Runs only when PLUS_AUTH_TEST_DB holds a connection string to a disposable PlusEMU
/// database with Resources/SQLs/Updates/19_SecureLoginTokens.sql applied.
/// </summary>
public sealed class AuthDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_AUTH_TEST_DB";

    public AuthDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a disposable PlusEMU database to run.";
    }
}

internal sealed class AuthTestDatabase : IDatabase
{
    public static readonly string ConnectionString = Environment.GetEnvironmentVariable(AuthDatabaseFactAttribute.Variable) ?? "";

    public bool IsConnected() => true;
    public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    public IDbConnection Connection() => new MySqlConnection(ConnectionString);

    public static int InsertUser(string username, string password = "", string mail = "probe@invalid")
    {
        using var connection = new MySqlConnection(ConnectionString);
        return connection.ExecuteScalar<int>("INSERT INTO users (username, password, mail, auth_ticket) VALUES (@username, @password, @mail, ''); SELECT LAST_INSERT_ID();",
            new { username, password, mail });
    }

    public static void DeleteUsers(IEnumerable<int> ids)
    {
        using var connection = new MySqlConnection(ConnectionString);
        connection.Execute("DELETE FROM user_access_tokens WHERE user_id IN @ids; DELETE FROM user_statistics WHERE id IN @ids; DELETE FROM users WHERE id IN @ids;",
            new { ids = ids.ToArray() });
    }

    public static string UniqueName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..(15 - prefix.Length)];
}

internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance(TimeSpan by) => Now += by;
}

internal static class AuthTestConfig
{
    public static IOptions<AuthApiConfiguration> Options(Action<AuthApiConfiguration>? configure = null)
    {
        var configuration = new AuthApiConfiguration();
        configure?.Invoke(configuration);
        return Microsoft.Extensions.Options.Options.Create(configuration);
    }
}

internal sealed class FakeAccounts : IAccountStore
{
    public readonly List<AccountCredentials> Rows = [];
    public readonly List<NewAccount> Created = [];
    public readonly HashSet<string> Emails = new(StringComparer.OrdinalIgnoreCase);

    public AccountCredentials Add(string username, string? password)
    {
        var row = new AccountCredentials(Rows.Count + 1, username, password);
        Rows.Add(row);
        return row;
    }

    public Exception? FailLookupsWith;
    public Task? HoldLookups;
    public readonly TaskCompletionSource LookupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<AccountCredentials?> FindByUsername(string username)
    {
        LookupStarted.TrySetResult();
        if (HoldLookups != null)
            await HoldLookups;
        if (FailLookupsWith != null)
            throw FailLookupsWith;
        // Like the users.username collation: case- and accent-insensitive.
        return Rows.FirstOrDefault(r => System.Globalization.CultureInfo.InvariantCulture.CompareInfo.Compare(r.Username, username,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) == 0);
    }

    public Task UpgradePassword(int userId, string current, string replacement)
    {
        var index = Rows.FindIndex(r => r.Id == userId && r.Password == current);
        if (index >= 0)
            Rows[index] = Rows[index] with { Password = replacement };
        return Task.CompletedTask;
    }

    public Task<string?> UsernameById(int userId) => Task.FromResult(Rows.FirstOrDefault(r => r.Id == userId)?.Username);

    public Task<bool> UsernameExists(string username) => Task.FromResult(Rows.Any(r => string.Equals(r.Username, username, StringComparison.OrdinalIgnoreCase)));

    /// <summary>When set, each email check waits (up to 100 ms) until this many checks are in
    /// flight, so unserialized check-then-insert races are guaranteed to overlap.</summary>
    public int ConcurrentEmailChecks;
    private int _emailChecksArrived;
    private readonly TaskCompletionSource _emailChecksGate = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> EmailExists(string email)
    {
        if (Interlocked.Increment(ref _emailChecksArrived) >= ConcurrentEmailChecks)
            _emailChecksGate.TrySetResult();
        await Task.WhenAny(_emailChecksGate.Task, Task.Delay(100));
        lock (Emails)
            return Emails.Contains(email);
    }

    /// <summary>Runs right after an account is created, before its session is issued.</summary>
    public Func<int, Task>? RevokeOnCreate;

    public async Task<int?> Create(NewAccount account)
    {
        var id = await CreateRow(account);
        if (id != null && RevokeOnCreate != null)
            await RevokeOnCreate(id.Value);
        return id;
    }

    private async Task<int?> CreateRow(NewAccount account)
    {
        await Task.Yield();
        if (await UsernameExists(account.Username))
            return null;
        lock (Emails)
        {
            Created.Add(account);
            Emails.Add(account.Email);
            return Add(account.Username, account.PasswordHash).Id;
        }
    }
}

internal sealed class FakeSsoTickets : ISsoTicketStore
{
    public readonly Dictionary<string, int> Live = [];

    public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null)
    {
        var token = new IssuedToken(SecureToken.Generate(), 1000);
        Live[token.Value] = userId;
        return Task.FromResult(token);
    }

    public Task<int?> FindUser(string ticket) => Task.FromResult(Live.TryGetValue(ticket, out var id) ? id : (int?)null);

    public Task<int?> Consume(string ticket)
    {
        Exchanged.Remove(ticket);
        return Task.FromResult(Live.Remove(ticket, out var id) ? id : (int?)null);
    }

    public readonly HashSet<string> Exchanged = [];

    public Task<int?> Exchange(string ticket) =>
        Task.FromResult(Live.TryGetValue(ticket, out var id) && Exchanged.Add(ticket) ? id : (int?)null);

    public Task Revoke(int userId, CredentialScope? scope = null)
    {
        foreach (var ticket in Live.Where(p => p.Value == userId).Select(p => p.Key).ToList())
            Live.Remove(ticket);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccessTokens : IAccessTokenStore
{
    public readonly Dictionary<string, int> Live = [];

    public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null)
    {
        var token = new IssuedToken(SecureToken.Generate(), 2000);
        Live[token.Value] = userId;
        return Task.FromResult(token);
    }

    public Task<int?> FindUser(string token) => Task.FromResult(Live.TryGetValue(token, out var id) ? id : (int?)null);

    public Task Revoke(string token)
    {
        Live.Remove(token);
        return Task.CompletedTask;
    }

    public Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        foreach (var key in Live.Where(p => p.Value == userId).Select(p => p.Key).ToList())
            Live.Remove(key);
        return Task.CompletedTask;
    }
}

internal sealed class FakeWordFilter(params string[] words) : IWordFilterManager
{
    public void Init() { }
    public string CheckMessage(string message) => message;
    public bool CheckBannedWords(string message) => false;
    public bool IsFiltered(string message) => words.Any(message.Contains);
}

internal sealed class FakeBans : IBanLookup
{
    public readonly Dictionary<string, LoginBan> ByUsernameOrAddress = new(StringComparer.OrdinalIgnoreCase);

    public Task<LoginBan?> Find(string username, string address) =>
        Task.FromResult(ByUsernameOrAddress.TryGetValue(username, out var ban) || ByUsernameOrAddress.TryGetValue(address, out ban) ? ban : null);
}

/// <summary>Real hashing slowed down, recording how many hashes ran at the same time.</summary>
internal sealed class CountingHasher(IPasswordHasher inner, TimeSpan delay) : IPasswordHasher
{
    private int _current;
    public int MaxConcurrent;

    public string Hash(string password) => Measure(() => inner.Hash(password));
    public PasswordVerificationResult Verify(string password, string stored) => Measure(() => inner.Verify(password, stored));

    private T Measure<T>(Func<T> work)
    {
        var now = Interlocked.Increment(ref _current);
        InterlockedMax(ref MaxConcurrent, now);
        try
        {
            Thread.Sleep(delay);
            return work();
        }
        finally
        {
            Interlocked.Decrement(ref _current);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen) { }
    }
}

/// <summary>In-memory remember tokens with the same rotation and reuse rules as the database store.</summary>
internal sealed class FakeRememberTokens : IRememberTokenStore
{
    private sealed record Row(int UserId, string Family, bool Used, bool Revoked);
    private readonly Dictionary<string, Row> _rows = [];
    private int _families;

    public Task<IssuedToken> Issue(int userId, CredentialScope? scope = null) => Task.FromResult(Add(userId, "f" + ++_families));

    public Task<IssuedToken> Continue(int userId, string familyId, CredentialScope? scope = null) => Task.FromResult(Add(userId, familyId));

    public Task<int> Prune(long cutoff, int batch) => Task.FromResult(0);

    public Task<RememberRotation> Rotate(string token)
    {
        if (!_rows.TryGetValue(token, out var row))
            return Task.FromResult(new RememberRotation(RememberRotationStatus.Invalid));
        if (row.Used)
        {
            RevokeWhere(r => r.Family == row.Family);
            return Task.FromResult(new RememberRotation(RememberRotationStatus.Reused, row.UserId, row.Family));
        }
        if (row.Revoked)
            return Task.FromResult(new RememberRotation(RememberRotationStatus.Invalid));
        _rows[token] = row with { Used = true };
        return Task.FromResult(new RememberRotation(RememberRotationStatus.Rotated, row.UserId, row.Family));
    }

    public Task RevokeFamily(string token)
    {
        if (_rows.TryGetValue(token, out var row))
            RevokeWhere(r => r.Family == row.Family);
        return Task.CompletedTask;
    }

    public Task RevokeAll(int userId, CredentialScope? scope = null)
    {
        RevokeWhere(r => r.UserId == userId);
        return Task.CompletedTask;
    }

    public bool IsLive(string token) => _rows.TryGetValue(token, out var row) && !row.Used && !row.Revoked;

    private IssuedToken Add(int userId, string family)
    {
        var token = new IssuedToken(SecureToken.Generate(), 3000);
        _rows[token.Value] = new(userId, family, false, false);
        return token;
    }

    private void RevokeWhere(Func<Row, bool> match)
    {
        foreach (var (key, row) in _rows.Where(p => match(p.Value)).ToList())
            _rows[key] = row with { Revoked = true };
    }
}

/// <summary>In-memory credential generations; writes run without a database scope.</summary>
internal sealed class FakeGenerations : ICredentialGenerations
{
    private readonly Dictionary<int, long> _generations = [];

    public Task<long> Current(int userId) => Task.FromResult(_generations.GetValueOrDefault(userId));

    public async Task<bool> WriteIfCurrent(int userId, long generation, Func<CredentialScope, Task> writes)
    {
        if (_generations.GetValueOrDefault(userId) != generation)
            return false;
        await writes(null!);
        return true;
    }

    public async Task Revoke(int userId, Func<CredentialScope, Task> revocations)
    {
        _generations[userId] = _generations.GetValueOrDefault(userId) + 1;
        await revocations(null!);
    }
}
