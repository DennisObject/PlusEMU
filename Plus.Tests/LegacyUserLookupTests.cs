using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

[Collection("HousekeepingDatabase")]
public sealed class LegacyUserLookupTests
{
    [Fact]
    public void AnOnlineUserReplacesTheCachedSnapshotWithoutReturningTheRemovedSnapshot()
    {
        const int userId = 937001;
        var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var cache = (ConcurrentDictionary<int, Habbo>)typeof(PlusEnvironment).GetField("_usersCached", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var oldGame = gameField.GetValue(null);
        var hadCache = cache.TryGetValue(userId, out var oldCached);
        var snapshot = new Habbo { Id = userId, Username = "old" };
        var online = new Habbo { Id = userId, Username = "live" };
        var clients = new HousekeepingActionTests.FakeClients();
        var (client, _) = HabbiconTestSupport.Client(online);
        clients.Online[userId] = client;
        var game = DispatchProxy.Create<IGame, ClientManagerProxy>();
        ((ClientManagerProxy)(object)game).Clients = clients;

        try
        {
            gameField.SetValue(null, game);
            cache[userId] = snapshot;
            Assert.Same(online, PlusEnvironment.GetHabboById(userId));
            Assert.False(cache.ContainsKey(userId));
            clients.Online.Clear();
            Assert.Null(PlusEnvironment.GetHabboById(userId));
            cache[userId] = snapshot;
            Assert.Same(snapshot, PlusEnvironment.GetHabboById(userId));
        }
        finally
        {
            gameField.SetValue(null, oldGame);
            cache.TryRemove(userId, out _);

            if (hadCache)
            {
                cache[userId] = oldCached!;
            }
        }
    }

    private class ClientManagerProxy : DispatchProxy
    {
        public HousekeepingActionTests.FakeClients Clients { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "get_ClientManager"
            ? Clients : throw new NotSupportedException(method?.Name);
    }
}
