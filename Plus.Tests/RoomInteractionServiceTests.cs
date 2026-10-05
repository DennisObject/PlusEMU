using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items.DataFormat;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Stickys;
using Plus.Communication.Packets.Outgoing;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class RoomInteractionServiceTests
{
    [Fact]
    public void RatingRejectsInvalidOwnerAndDuplicateRequests()
    {
        var store = new RecordingStore();
        var service = new RoomInteractionService(store);
        var (room, owner, _) = Context("owner");
        service.Rate(room, owner, 2);
        service.Rate(room, owner, 1);
        var (otherRoom, voter, _) = Context("owner", "voter");
        voter.GetHabbo().RatedRooms.Add(otherRoom.RoomId);
        service.Rate(otherRoom, voter, 1);

        Assert.Equal(0, store.Ratings);
        Assert.Equal(0, room.Score);
        Assert.Equal(0, otherRoom.Score);
    }

    [Fact]
    public void SuccessfulRatingPublishesCommittedScoreOnce()
    {
        var (room, client, sent) = Context("owner", "voter");
        var store = new RecordingStore { ResultingScore = 42 };
        var service = new RoomInteractionService(store);

        service.Rate(room, client, -1);
        service.Rate(room, client, -1);

        Assert.Equal(1, store.Ratings);
        Assert.Equal(42, room.Score);
        Assert.Contains(room.RoomId, client.GetHabbo().RatedRooms);
        Assert.Single(sent);
    }

    [Fact]
    public void RatingFailureDoesNotPublishMemoryOrPacket()
    {
        var (room, client, sent) = Context("owner", "voter");
        var store = new RecordingStore { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new RoomInteractionService(store).Rate(room, client, 1));

        Assert.Equal(0, room.Score);
        Assert.Empty(client.GetHabbo().RatedRooms);
        Assert.Empty(sent);
    }

    [Fact]
    public void StickyWrongTypeAndPersistenceFailureLeaveFurnitureInRoom()
    {
        var (room, client, _) = Context("owner");
        var item = AddItem(room, InteractionType.Gate);
        var store = new RecordingStore();
        var service = new RoomInteractionService(store);
        service.DeleteSticky(room, client, item.Id);
        item.Definition.InteractionType = InteractionType.Postit;
        store.Fail = true;

        Assert.Throws<InvalidOperationException>(() => service.DeleteSticky(room, client, item.Id));
        Assert.Same(item, room.GetRoomItemHandler().GetItem(item.Id));
        Assert.Equal(1, store.Deletions);
    }

    [Fact]
    public async Task StickyHandlersDecodeCompletelyAndDelegateWithoutRoomRuntime()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var service = new RecordingInteractions();
        var read = HabbiconTestSupport.Incoming(-2);
        var edit = HabbiconTestSupport.Incoming(-2, "FFFF33", "hello");
        await new GetStickyNoteEvent(service).Parse(room, client, read);
        await new UpdateStickyNoteEvent(service).Parse(room, client, edit);
        Assert.Equal(new[] { "show 4294967294", "edit 4294967294 FFFF33 hello" }, service.Calls);
        Assert.False(read.HasDataRemaining());
        Assert.False(edit.HasDataRemaining());
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StickyEditPersistsBeforeStateOrPackets(bool fail)
    {
        var (room, client, sent) = Context("owner");
        var item = AddItem(room, InteractionType.Postit);
        item.LegacyDataString = "FFFF33 old";
        var store = new RecordingStore { Fail = fail, BeforeUpdate = () =>
        {
            Assert.Equal("FFFF33 old", item.LegacyDataString);
            Assert.Empty(sent);
        }};
        var service = new RoomInteractionService(store);
        if (fail)
        {
            Assert.Throws<InvalidOperationException>(() => service.UpdateSticky(room, client, item.Id, "9CCEFF", "edited"));
            Assert.Equal("FFFF33 old", item.LegacyDataString);
            Assert.Empty(sent);
        }
        else
        {
            service.UpdateSticky(room, client, item.Id, "9CCEFF", "edited");
            Assert.Equal("9CCEFF edited", item.LegacyDataString);
            Assert.Single(sent);
            Assert.Equal(ServerPacketHeader.ItemUpdateComposer, sent[0].Header);
        }
        Assert.Equal("9CCEFF edited", store.UpdatedData);
        Assert.Equal(1, store.Updates);
    }

    [Fact]
    public void StickyReadsCaptureDataAndEditsPreserveAppendAndColourPolicy()
    {
        var (room, client, sent) = Context("owner", "visitor");
        var item = AddItem(room, InteractionType.Postit);
        item.LegacyDataString = "FFFF33 old";
        var store = new RecordingStore();
        var service = new RoomInteractionService(store);
        service.ShowSticky(room, client, item.Id);
        var read = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal("7", read.ReadString());
        Assert.Equal("FFFF33 old", read.ReadString());
        Assert.False(read.HasDataRemaining());
        sent.Clear();
        service.UpdateSticky(room, client, item.Id, "FFFF33", "replacement");
        service.UpdateSticky(room, client, item.Id, "bad", "FFFF33 old appended");
        Assert.Equal(0, store.Updates);
        Assert.Empty(sent);
        service.UpdateSticky(room, client, item.Id, "FF9CFF", "FFFF33 old appended");
        Assert.Equal("FF9CFF FFFF33 old appended", item.LegacyDataString);
        Assert.Equal(1, store.Updates);
        sent.Clear();
        item.Definition.InteractionType = InteractionType.Gate;
        service.ShowSticky(room, client, item.Id);
        service.UpdateSticky(room, client, item.Id, "FFFF33", item.LegacyDataString);
        item.Definition.InteractionType = InteractionType.Postit;
        client.GetHabbo().CurrentRoom = null;
        service.ShowSticky(room, client, item.Id);
        service.UpdateSticky(room, client, item.Id, "FFFF33", item.LegacyDataString);
        Assert.Empty(sent);
        Assert.Equal(1, store.Updates);
    }

    [RoomComponentDatabaseFact]
    public void StickyUpdateTargetsTheExactRoomAndReportsFailureWithoutChangingAnotherNote()
    {
        var root = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        using var server = new MySqlConnection(root);
        server.Open();
        var schema = "task_refactor_tests_sticky_" + Guid.NewGuid().ToString("N");
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var connectionString = new MySqlConnectionStringBuilder(root)
            {
                Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true
            }.ConnectionString;
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            connection.Execute("CREATE TABLE items(id INT PRIMARY KEY,room_id INT NOT NULL,extra_data TEXT NOT NULL); INSERT INTO items VALUES(7,9,'old'),(8,10,'other')");
            var store = new RoomInteractionStore(new StickyDatabase(connectionString));
            Assert.Throws<InvalidOperationException>(() => store.UpdateSticky(7, 10, "wrong room"));
            Assert.Equal("old", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=7"));
            store.UpdateSticky(7, 9, "FFFF33 edited");
            Assert.Equal("FFFF33 edited", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=7"));
            Assert.Equal("other", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=8"));
            connection.Execute("CREATE TRIGGER reject_edit BEFORE UPDATE ON items FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced sticky failure'");
            Assert.Throws<MySqlException>(() => store.UpdateSticky(7, 9, "failure"));
            Assert.Equal("FFFF33 edited", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=7"));
        }
        finally { server.Execute($"DROP DATABASE `{schema}`"); }
    }

    private sealed class StickyDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class RecordingInteractions : IRoomInteractionService
    {
        public List<string> Calls { get; } = [];
        public void ShowSticky(Room room, GameClient session, uint itemId) => Calls.Add($"show {itemId}");
        public void UpdateSticky(Room room, GameClient session, uint itemId, string colour, string text) => Calls.Add($"edit {itemId} {colour} {text}");
        public void Rate(Room room, GameClient session, int rating) => throw new NotSupportedException();
        public void DeleteSticky(Room room, GameClient session, uint itemId) => throw new NotSupportedException();
    }

    private static (Room Room, GameClient Client, List<(uint Header, byte[] Payload)> Sent) Context(string owner, string username = "owner")
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 9; room.OwnerName = owner; room.Type = "private"; room.UsersWithRights = [];
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance));
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = username == owner ? 1 : 2, Username = username, CurrentRoom = room });
        client.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();
            sent.Add((System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2)), bytes[6..]));
            return true;
        };
        var viewer = new RoomUser(client.GetHabbo().Id, room.Id, 1, room, client);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users[1] = viewer;
        return (room, client, sent);
    }

    private static Item AddItem(Room room, InteractionType type)
    {
        var item = new Item { Id = 7, RoomId = room.Id, OwnerId = 1, Definition = new() { InteractionType = type, Type = type == InteractionType.Postit ? ItemType.Wall : ItemType.Floor }, ExtraData = new LegacyDataFormat { Data = "original" } };
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomItemHandler())!;
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        floor[item.Id] = item;
        return item;
    }

    private sealed class RecordingStore : IRoomInteractionStore
    {
        public int ResultingScore { get; init; }
        public bool Fail { get; set; }
        public Action? BeforeUpdate;
        public string? UpdatedData;
        public int Updates;
        public void UpdateSticky(uint itemId, uint roomId, string data) { BeforeUpdate?.Invoke(); Updates++; UpdatedData = data; if (Fail) throw new InvalidOperationException("forced"); }
        public int Ratings { get; private set; }
        public int Deletions { get; private set; }
        public int AddRating(uint roomId, int rating) { Ratings++; if (Fail) throw new InvalidOperationException("forced"); return ResultingScore; }
        public void DeleteSticky(uint itemId, uint roomId) { Deletions++; if (Fail) throw new InvalidOperationException("forced"); }
    }
}
