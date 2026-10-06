using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.Cache.Type;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.Communication.Packets.Incoming.Rooms.Settings;
using Plus.Communication.Packets.Outgoing;
using System.Buffers.Binary;
using System.Text;
using Xunit;

namespace Plus.Tests;

public sealed class RoomRightsServiceTests
{
    [Fact]
    public async Task RightsListHandlerDelegatesWithoutRequiringRoomState()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var rights = new RecordingRights();
        await new GetRoomRightsEvent(rights).Parse(client, HabbiconTestSupport.Incoming());
        Assert.Same(client, rights.Session);
        Assert.Empty(sent);
    }

    [Fact]
    public void RightsListPreservesOrderAndMissingUserPlaceholder()
    {
        var room = TestRoom();
        room.UsersWithRights = [2, 99];
        var (owner, sent) = HabbiconTestSupport.Client(new Habbo
        {
            Id = 1, Username = "owner", CurrentRoom = room, Access = EditorTestSupport.Access([])
        });
        var service = new RoomRightsService(new RecordingStore(), null!, new Cache(new() { Id = 2, Username = "guest" }));
        service.Show(owner);
        var response = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.RoomRightsListComposer, response.Header);
        var expected = new List<byte>();
        void Integer(int value) { var bytes = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(bytes, value); expected.AddRange(bytes); }
        void String(string value) { var bytes = Encoding.UTF8.GetBytes(value); var length = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)bytes.Length); expected.AddRange(length); expected.AddRange(bytes); }
        Integer(42); Integer(2); Integer(2); String("guest"); Integer(0); String("Unknown Error");
        Assert.Equal(expected, response.Payload);
    }

    [Fact]
    public void RightsListDeniesVisitorsAndMissingRoomsBeforeCacheLookup()
    {
        var room = TestRoom();
        room.UsersWithRights = [2];
        var (visitor, sent) = HabbiconTestSupport.Client(new Habbo
        {
            Id = 3, Username = "visitor", CurrentRoom = room, Access = EditorTestSupport.Access([])
        });
        var cache = new Cache(null);
        var service = new RoomRightsService(new RecordingStore(), null!, cache);
        service.Show(visitor);
        visitor.GetHabbo().CurrentRoom = null!;
        service.Show(visitor);
        Assert.Equal(0, cache.Reads);
        Assert.Empty(sent);
    }

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
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
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
        public int Reads;
        public CachedUser? GenerateUser(int id) { Reads++; return user?.Id == id ? user : null; }
        public bool ContainsUser(int id) => false;
        public bool TryRemoveUser(int id, out CachedUser cachedUser) { cachedUser = null!; return false; }
        public bool TryGetUser(int id, out CachedUser cachedUser) { cachedUser = null!; return false; }
        public ICollection<CachedUser> GetUserCache() => [];
        public void Init() { }
    }

    private sealed class RecordingRights : IRoomRightsService
    {
        public Plus.HabboHotel.GameClients.GameClient? Session;
        public void Show(Plus.HabboHotel.GameClients.GameClient session) => Session = session;
        public void Assign(Room room, Plus.HabboHotel.GameClients.GameClient session, int userId) => throw new NotSupportedException();
        public void Remove(Room room, Plus.HabboHotel.GameClients.GameClient session, IReadOnlyList<int> userIds) => throw new NotSupportedException();
        public void RemoveAll(Room room, Plus.HabboHotel.GameClients.GameClient session) => throw new NotSupportedException();
        public void RemoveOwn(Room room, Plus.HabboHotel.GameClients.GameClient session) => throw new NotSupportedException();
    }
}
