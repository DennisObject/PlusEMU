using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Cache.Process;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Cache;

public class CacheManager : ICacheManager, IStartable
{
    private readonly ILogger<CacheManager> _logger;
    private readonly IProcessComponent _process;
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<int, CachedUser> _usersCached;

    public CacheManager(IProcessComponent processComponent, IDatabase database, IGameClientManager gameClientManager, ILogger<CacheManager> logger, TimeProvider clock)
    {
        _process = processComponent;
        _database = database;
        _gameClientManager = gameClientManager;
        _logger = logger;
        _clock = clock;
        _usersCached = new();
    }

    public int StartOrder => 90;
    public Task Start()
    {
        Init();

        return Task.CompletedTask;
    }

    public void Init()
    {
        _process.Init(Sweep);
        _logger.LogInformation("Cache Manager -> LOADED");
    }

    public bool ContainsUser(int id) => _usersCached.ContainsKey(id);

    public CachedUser? GenerateUser(int id)
    {
        var now = _clock.GetUtcNow();
        CachedUser? cachedUser;

        while (TryGetUser(id, out cachedUser)) {
            var refreshed = cachedUser.RefreshAt(now);

            if (_usersCached.TryUpdate(id, refreshed, cachedUser)) {
                return refreshed;
            }
        }

        var client = _gameClientManager.GetClientByUserId(id);

        if (client?.GetHabbo() is { } habbo) {
            cachedUser = new() { Id = id, Username = habbo.Username, Motto = habbo.Motto, Look = habbo.Look, RefreshedAt = now };

            return AddOrRefresh(id, cachedUser, now);
        }

        using var connection = _database.Connection();
        cachedUser = connection.QuerySingleOrDefault<CachedUser>("SELECT id, `username`, `motto`, `look` FROM users WHERE id = @id LIMIT 1", new { id });

        return cachedUser == null ? null : AddOrRefresh(id, cachedUser.RefreshAt(now), now);
    }

    private CachedUser AddOrRefresh(int id, CachedUser candidate, DateTimeOffset now)
    {
        while (true) {
            if (_usersCached.TryAdd(id, candidate)) {
                return candidate;
            }

            if (_usersCached.TryGetValue(id, out var current)) {
                var refreshed = current.RefreshAt(now);

                if (_usersCached.TryUpdate(id, refreshed, current)) {
                    return refreshed;
                }
            }
        }
    }

    internal void Sweep()
    {
        var now = _clock.GetUtcNow();

        foreach (var entry in _usersCached.ToArray()) {
            RemoveIfExpired(entry, now);
        }

        foreach (var user in PlusEnvironment.RemoveExpiredCachedUsers(now)) {
            try {
                user.Dispose();
            }
            catch (Exception e) {
                _logger.LogError(e, "Failed to dispose expired legacy cached user {UserId}", user.Id);
            }
        }
    }

    internal bool RemoveIfExpired(KeyValuePair<int, CachedUser> entry, DateTimeOffset now) =>
        entry.Value.IsExpiredAt(now) && ((ICollection<KeyValuePair<int, CachedUser>>)_usersCached).Remove(entry);

    public bool TryRemoveUser(int id, [NotNullWhen(true)] out CachedUser? cachedUser) => _usersCached.TryRemove(id, out cachedUser);

    public bool TryGetUser(int id, [NotNullWhen(true)] out CachedUser? cachedUser) => _usersCached.TryGetValue(id, out cachedUser);

    public ICollection<CachedUser> GetUserCache() => _usersCached.Values;
}
