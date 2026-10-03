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
        return Rows.FirstOrDefault(r => string.Equals(r.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public Task UpgradePassword(int userId, string current, string replacement)
    {
        var index = Rows.FindIndex(r => r.Id == userId && r.Password == current);
        if (index >= 0)
            Rows[index] = Rows[index] with { Password = replacement };
        return Task.CompletedTask;
    }

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

    public async Task<int?> Create(NewAccount account)
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

    public Task<IssuedToken> Issue(int userId)
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

    public Task Revoke(int userId)
    {
        foreach (var ticket in Live.Where(p => p.Value == userId).Select(p => p.Key).ToList())
            Live.Remove(ticket);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAccessTokens : IAccessTokenStore
{
    public readonly Dictionary<string, int> Live = [];

    public Task<IssuedToken> Issue(int userId)
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

    public Task RevokeAll(int userId)
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

internal sealed class FakeModeration : IModerationManager
{
    public readonly Dictionary<string, ModerationBan> Bans = new(StringComparer.OrdinalIgnoreCase);

    public bool IsBanned(string key, out ModerationBan ban) => Bans.TryGetValue(key, out ban!);

    public ICollection<string> UserMessagePresets => [];
    public ICollection<string> RoomMessagePresets => [];
    public ICollection<ModerationTicket> GetTickets => [];
    public Dictionary<string, List<ModerationPresetActions>> UserActionPresets => [];
    public void Init() { }
    public void ReCacheBans() { }
    public void BanUser(string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp) => throw new NotSupportedException();
    public bool TryAddTicket(ModerationTicket ticket) => throw new NotSupportedException();
    public bool TryGetTicket(int ticketId, out ModerationTicket ticket) => throw new NotSupportedException();
    public bool UserHasTickets(int userId) => false;
    public ModerationTicket GetTicketBySenderId(int userId) => throw new NotSupportedException();
    public bool HasMachineBanCheck(string machineId) => false;
    public bool UsernameBanCheck(string username) => Bans.ContainsKey(username);
    public void RemoveBan(string value) => Bans.Remove(value);
}
