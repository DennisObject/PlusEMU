using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RoomRightsServiceTests
{
    [Fact]
    public void OwnerAssignmentPersistsBeforePublishingRights()
    {
        var room = TestRoom();
        var (owner, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "owner", Access = EditorTestSupport.Access([]) });
        var store = new RecordingStore();
        var service = new RoomRightsService(store, null!, new Cache(new() { Id = 2, Username = "guest" }));

        service.Assign(room, owner, 2);

        Assert.Equal((room.Id, 2), Assert.Single(store.Assignments));
        Assert.Contains(2, room.UsersWithRights);
        Assert.Single(sent);
    }

    [Fact]
    public void NonOwnerAssignmentIsALegitimateNoOp()
    {
        var room = TestRoom();
        var (guest, sent) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "guest", Access = EditorTestSupport.Access([]) });
        var store = new RecordingStore();
        var service = new RoomRightsService(store, null!, new Cache(null));

        service.Assign(room, guest, 3);

        Assert.Empty(store.Assignments);
        Assert.Empty(room.UsersWithRights);
        Assert.Empty(sent);
    }

    [Fact]
    public void PersistenceFailureDoesNotPublishRightsInMemory()
    {
        var room = TestRoom();
        var (owner, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "owner", Access = EditorTestSupport.Access([]) });
        var service = new RoomRightsService(new RecordingStore { Fail = true }, null!, new Cache(null));

        Assert.Throws<InvalidOperationException>(() => service.Assign(room, owner, 2));
        Assert.Empty(room.UsersWithRights);
        Assert.Empty(sent);
    }

    private static Room TestRoom()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        room.OwnerName = "owner";
        room.Type = "private";
        room.UsersWithRights = [];
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        return room;
    }

    private sealed class RecordingStore : IRoomRightsStore
    {
        public bool Fail { get; init; }
        public List<(uint RoomId, int UserId)> Assignments { get; } = [];
        public void Assign(uint roomId, int userId)
        {
            if (Fail) throw new InvalidOperationException("forced failure");
            Assignments.Add((roomId, userId));
        }
        public void Remove(uint roomId, IReadOnlyList<int> userIds)
        {
            if (Fail) throw new InvalidOperationException("forced failure");
        }
    }

    private sealed class Cache(CachedUser? user) : ICacheManager
    {
        public CachedUser? GenerateUser(int id) => user?.Id == id ? user : null;
        public bool ContainsUser(int id) => false;
        public bool TryRemoveUser(int id, out CachedUser cachedUser) { cachedUser = null!; return false; }
        public bool TryGetUser(int id, out CachedUser cachedUser) { cachedUser = null!; return false; }
        public ICollection<CachedUser> GetUserCache() => [];
        public void Init() { }
    }
}
