using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Xunit;

namespace Plus.Tests;

public sealed class RoomBanTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExactExpiryBoundaryDeletesAfterCapturedNow()
    {
        var store = new RecordingStore();
        var bans = Component(store, new RoomBan(7, Now));

        Assert.False(bans.IsBanned(7));
        Assert.Equal([(42u, 7)], store.Deleted);
        Assert.Equal(0, bans.Count);
    }

    [Fact]
    public void FutureExpiryRemainsActive()
    {
        var store = new RecordingStore();
        var bans = Component(store, new RoomBan(7, Now.AddTicks(1)));

        Assert.True(bans.IsBanned(7));
        Assert.Empty(store.Deleted);
    }

    [Fact]
    public void CleanupIsRepeatableAndDetachedOperationsAreRejected()
    {
        var bans = Component(new RecordingStore(), new RoomBan(7, Now.AddHours(1)));

        bans.Cleanup();
        bans.Cleanup();

        Assert.Equal(0, bans.Count);
        Assert.Throws<ObjectDisposedException>(() => bans.IsBanned(7));
        Assert.Throws<ObjectDisposedException>(() => bans.BannedUsers());
    }

    [Fact]
    public void FirstLifecyclePhasePublishesAnEmptyQueryFreeComponent()
    {
        var room = Room(42);
        var component = new RoomBansComponent(new FailingDatabase(), new FixedClock(Now));

        component.Initiate(room);

        Assert.Equal(0, room.GetBans().Count);
    }

    private static BansComponent Component(IRoomBanStore store, params RoomBan[] rows) =>
        new(Room(42), store, new FixedClock(Now), rows);

    private static Room Room(uint id)
    {
        var data = (RoomData)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
        data.Id = id;

        return new Room(data, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingStore : IRoomBanStore
    {
        public List<(uint RoomId, int UserId)> Deleted { get; } = [];
        public IEnumerable<RoomBan> Load(uint roomId) => [];
        public void Save(uint roomId, int userId, DateTimeOffset expiresAt)
        {
        }
        public void Delete(uint roomId, int userId) => Deleted.Add((roomId, userId));
        public IEnumerable<int> ActiveUserIds(uint roomId) => [];
    }

    private sealed class FailingDatabase : Plus.Database.IDatabase
    {
        public bool IsConnected() => false;
        public System.Data.IDbConnection Connection() => throw new InvalidOperationException("First phase must not query persistence.");
    }
}
