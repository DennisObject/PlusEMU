using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public sealed class LoveLockServiceTests
{
    [Fact]
    public void MissingParticipantsCancelSafelyWithoutPersistence()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9;
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = InteractionType.Lovelock }, InteractingUser = 1, InteractingUser2 = 2 };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        floor[item.Id] = item;
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, CurrentRoom = room });
        var store = new RecordingStore();

        new LoveLockService(store, TimeProvider.System).Confirm(client, new(item.Id, true));

        Assert.Equal(0, item.InteractingUser);
        Assert.Equal(0, item.InteractingUser2);
        Assert.Equal(0, store.Writes);
        Assert.Single(sent);
    }

    [Fact]
    public void PersistenceFailureLeavesItemAndConfirmationStateUnchanged()
    {
        var room = TestRoom();
        var (oneClient, _) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "one", Look = "look1", CurrentRoom = room });
        var (twoClient, sent) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "two", Look = "look2", CurrentRoom = room });
        var one = AddUser(room, oneClient, 1); var two = AddUser(room, twoClient, 2);
        one.LlPartner = 2;
        var item = AddItem(room, 1, 2);
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new LoveLockService(store, TimeProvider.System).Confirm(twoClient, new(item.Id, true)));

        Assert.Equal(1, item.InteractingUser);
        Assert.Equal(2, item.InteractingUser2);
        Assert.Equal(2, one.LlPartner);
        Assert.Equal(0, two.LlPartner);
        Assert.Empty(sent);
    }

    [Fact]
    public void TwoConfirmationsStageThenPersistBeforeFinalPublication()
    {
        var room = TestRoom();
        var (oneClient, oneSent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "one", Look = "look1", CurrentRoom = room });
        var (twoClient, twoSent) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "two", Look = "look2", CurrentRoom = room });
        var one = AddUser(room, oneClient, 1); var two = AddUser(room, twoClient, 2);
        var item = AddItem(room, 1, 2);
        var store = new RecordingStore(() =>
        {
            Assert.Equal(1, item.InteractingUser);
            Assert.Equal(2, item.InteractingUser2);
            Assert.Equal("", item.ExtraData.Serialize());
            Assert.Empty(twoSent);
        });
        var service = new LoveLockService(store, new FixedTimeProvider(new DateTimeOffset(2040, 12, 31, 23, 0, 0, TimeSpan.Zero)));

        service.Confirm(oneClient, new(item.Id, true));

        Assert.Equal(0, store.Writes);
        var staged = Assert.Single(oneSent);
        Assert.Equal(ServerPacketHeader.FriendFurniOtherLockConfirmedComposer, staged.Header);
        Assert.Equal(item.Id, BinaryPrimitives.ReadUInt32BigEndian(staged.Payload));
        Assert.Equal(2, one.LlPartner);

        service.Confirm(twoClient, new(item.Id, true));

        Assert.Equal(1, store.Writes);
        Assert.Equal(0, item.InteractingUser);
        Assert.Equal(0, item.InteractingUser2);
        Assert.Contains(((char)5).ToString(), item.ExtraData.Serialize());
        Assert.EndsWith("31/12/2040", item.ExtraData.Serialize());
        Assert.Equal(item.ExtraData.Serialize(), store.Data);
        Assert.Equal(0, one.LlPartner);
        Assert.Equal(0, two.LlPartner);
        Assert.True(one.CanWalk);
        Assert.True(two.CanWalk);
        Assert.Contains(twoSent, packet => packet.Header == ServerPacketHeader.FriendFurniCancelLockComposer && BinaryPrimitives.ReadUInt32BigEndian(packet.Payload) == item.Id);
    }

    [Fact]
    public void NonOwnerParticipantContextIsDeniedWithoutMutation()
    {
        var room = TestRoom();
        var (oneClient, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "one", CurrentRoom = room });
        var (twoClient, _) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "two", CurrentRoom = room });
        AddUser(room, oneClient, 1); AddUser(room, twoClient, 2);
        var item = AddItem(room, 1, 2, ownerId: 99);
        var store = new RecordingStore();

        new LoveLockService(store, TimeProvider.System).Confirm(oneClient, new(item.Id, true));

        Assert.Equal(1, item.InteractingUser);
        Assert.Equal(2, item.InteractingUser2);
        Assert.Equal(0, store.Writes);
        Assert.Empty(sent);
    }

    [Fact]
    public void NonParticipantCannotCancelActiveLoveLock()
    {
        var room = TestRoom();
        var (oneClient, oneSent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "one", CurrentRoom = room });
        var (twoClient, twoSent) = HabbiconTestSupport.Client(new Habbo { Id = 2, Username = "two", CurrentRoom = room });
        var (intruderClient, intruderSent) = HabbiconTestSupport.Client(new Habbo { Id = 3, Username = "intruder", CurrentRoom = room });
        var one = AddUser(room, oneClient, 1); var two = AddUser(room, twoClient, 2); AddUser(room, intruderClient, 3);
        one.LlPartner = 2; two.LlPartner = 1;
        var item = AddItem(room, 1, 2);
        var store = new RecordingStore();

        new LoveLockService(store, TimeProvider.System).Confirm(intruderClient, new(item.Id, false));

        Assert.Equal(1, item.InteractingUser);
        Assert.Equal(2, item.InteractingUser2);
        Assert.Equal(2, one.LlPartner);
        Assert.Equal(1, two.LlPartner);
        Assert.Equal(0, store.Writes);
        Assert.Empty(oneSent);
        Assert.Empty(twoSent);
        Assert.Empty(intruderSent);
    }

    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("3.6.0.json")]
    [InlineData("OCTANE-3-6-0-FLOOR-20260909.json")]
    public void FinishedPacketIsMappedForEachActiveClientRevision(string revision)
    {
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Resources/Revisions/" + revision)));
        Assert.Equal(770u, json.RootElement.GetProperty("OutgoingHeaders").GetProperty("FriendFurniCancelLockComposer").GetUInt32());
    }

    private static Room TestRoom()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 9;
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        return room;
    }

    private static Item AddItem(Room room, int one, int two, uint ownerId = 1)
    {
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = ownerId, Definition = new() { InteractionType = InteractionType.Lovelock },
            ExtraData = new LegacyDataFormat(), InteractingUser = one, InteractingUser2 = two };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        floor[item.Id] = item; return item;
    }

    private static RoomUser AddUser(Room room, GameClient client, int id)
    {
        var user = new RoomUser(id, room.Id, id, room, client) { InternalRoomId = id };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users[id] = user; return user;
    }

    private sealed class RecordingStore(Action? beforeLock = null) : ILoveLockStore
    {
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public string? Data { get; private set; }
        public void Lock(uint itemId, uint roomId, string data) { beforeLock?.Invoke(); Writes++; Data = data; if (Fail) throw new InvalidOperationException("forced failure"); }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
