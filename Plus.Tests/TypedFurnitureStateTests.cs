using System.Collections.Concurrent;
using System.Collections.Immutable;
using Dapper;
using System.Reflection;
using System.Text.Json;
using Microsoft.IO;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class TypedFurnitureStateTests
{
    [Fact]
    public void CapturedFiftyOneScoresSurviveCanonicalStorageAndExistingTypeSixWire()
    {
        using var captured = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Plus.Tests/Fixtures/TypedFurnitureCapturedScores.json")));
        var total = 0;

        foreach (var board in captured.RootElement.EnumerateArray()) {
            var stored = board.GetProperty("Stored");
            var data = new HighscoreDataFormat();
            data.Store(stored.GetRawText());
            using var reloaded = JsonDocument.Parse(data.Serialize());
            Assert.True(JsonElement.DeepEquals(stored, reloaded.RootElement));
            var item = new Item { Id = 100, Definition = new() { Type = ItemType.Floor }, ExtraData = data };
            var wire = Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)));
            using var reader = new BinaryReader(new MemoryStream(wire));

            for (var i = 0; i < 5; i++) {
                ReadInt(reader);
            }

            ReadString(reader);
            ReadString(reader);
            ReadInt(reader);
            Assert.Equal(6, ReadInt(reader));
            Assert.Equal(stored.GetProperty("State").GetString(), ReadString(reader));
            Assert.Equal(stored.GetProperty("ScoreType").GetInt32(), ReadInt(reader));
            Assert.Equal(stored.GetProperty("ClearType").GetInt32(), ReadInt(reader));
            var count = ReadInt(reader);
            Assert.Equal(17, count);

            foreach (var entry in stored.GetProperty("Entries").EnumerateArray()) {
                Assert.Equal(entry.GetProperty("Score").GetInt32(), ReadInt(reader));
                var users = entry.GetProperty("Users").EnumerateArray().Select(value => value.GetString()).ToArray();
                Assert.Equal(users.Length, ReadInt(reader));
                Assert.Equal(users, users.Select(_ => ReadString(reader)).ToArray());
            }

            Assert.Equal(-1, ReadInt(reader));

            for (var i = 0; i < 9; i++) {
                Assert.Equal(0, ReadInt(reader));
            }

            Assert.Equal(reader.BaseStream.Length, reader.BaseStream.Position);
            total += count;
        }

        Assert.Equal(51, total);
    }

    [Fact]
    public void TonerWritePublishesTypedArrayFromDurableCompanion()
    {
        var world = TonerWorld();
        new RoomItemMetadataService(new Metadata(), null!).SetToner(world.Room, world.Client, new(100, 120, 141, 120));
        var data = Assert.IsType<FurnitureDataSnapshot.Integers>(RoomItemSnapshot.Capture(world.Chest).Data);
        Assert.Equal(new[] { 1, 120, 141, 120 }, data.Values);
    }

    [Fact]
    public void StoredScorePayloadRefusesMalformedVersionsAndNeverMutatesExistingRows()
    {
        var data = new HighscoreDataFormat();
        var valid = "{\"Version\":1,\"State\":\"1\",\"ScoreType\":1,\"ClearType\":0,\"Entries\":[{\"Score\":7,\"Users\":[\"historical-name\"]}]}";
        data.Store(valid);
        var before = data.Serialize();
        var malformed = new[] { "{}", valid + "tail", valid.Replace("\"Version\":1", "\"Version\":2"),
            valid.Replace("\"Users\":[\"historical-name\"]", "\"Users\":null"),
            valid.Replace("\"Score\":7", "\"Score\":2147483648"),
            valid.Replace("\"ClearType\":0", "\"ClearType\":4"), valid.Replace("\"State\":\"1\"", "\"State\":null"),
            valid.Replace("\"Version\":1", "\"Version\":1,\"Version\":1") };

        foreach (var payload in malformed) {
            Assert.Throws<ArgumentException>(() => data.Store(payload));
            Assert.Equal(before, data.Serialize());
        }
    }

    [Fact]
    public void StoredHistoryDoesNotApplyProducerRowLimit()
    {
        var data = new HighscoreDataFormat { State = "1", ScoreType = 1, Entries = Enumerable.Range(0, 61).Select(score => new HighscoreEntry(score, ["historical-name"])).ToImmutableArray() };
        var loaded = new HighscoreDataFormat();
        loaded.Store(data.Serialize());
        Assert.Equal(61, loaded.Entries.Length);
        Assert.Equal(data.Serialize(), loaded.Serialize());
        var item = new Item { Id = 100, Definition = new() { Type = ItemType.Floor }, ExtraData = loaded };
        using var reader = new BinaryReader(new MemoryStream(Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)))));

        for (var i = 0; i < 5; i++) {
            ReadInt(reader);
        }

        ReadString(reader);
        ReadString(reader);
        ReadInt(reader);
        ReadInt(reader);
        ReadString(reader);
        ReadInt(reader);
        ReadInt(reader);
        Assert.Equal(61, ReadInt(reader));
    }

    [Fact]
    public void OrderedNestedScoresRemainImmutableInPublishedSnapshot()
    {
        var data = new HighscoreDataFormat { State = "1", ScoreType = 1, Entries = [new(7, ["same", "same", "second"]), new(3, ["first"])] };
        var item = new Item { Id = 100, Definition = new() { Type = ItemType.Floor }, ExtraData = data };
        var composer = new ObjectUpdateComposer(RoomItemSnapshot.Capture(item));
        var wire = Payload(composer);
        data.Entries = [new(99, ["changed"])];
        data.State = "0";
        Assert.Equal(wire, Payload(composer));
        Assert.NotEqual(wire, Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item))));
    }

    [WiredChestDatabaseFact]
    public void ActualLoaderReloadsAllCapturedRowsAndLegacyScoreResetCannotEraseThem()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        PrepareItems(fixture);
        using var captured = JsonDocument.Parse(File.ReadAllText(HabbiconPacketTests.Repo("Plus.Tests/Fixtures/TypedFurnitureCapturedScores.json")));
        var definitions = new Dictionary<uint, ItemDefinition>();
        uint id = 500;

        foreach (var board in captured.RootElement.EnumerateArray()) {
            var data = new HighscoreDataFormat();
            data.Store(board.GetProperty("Stored").GetRawText());
            definitions[id] = new() { Id = id, Type = ItemType.Floor, ItemName = board.GetProperty("Classname").GetString()! };
            fixture.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,1,42,@id,@stored)", new { id, stored = data.Serialize() });
            id++;
        }

        var loader = new RoomFurnitureLoader(fixture.Database, new TestWiredDefinitions(() => definitions));
        var first = loader.Load(42).Where(item => definitions.ContainsKey(item.Definition.Id)).ToArray();
        var second = new RoomFurnitureLoader(fixture.Database, new TestWiredDefinitions(() => definitions)).Load(42).Where(item => definitions.ContainsKey(item.Definition.Id)).ToArray();
        Assert.Equal(51, first.Sum(item => Assert.IsType<HighscoreDataFormat>(item.ExtraData).Entries.Length));
        Assert.Equal(first.Select(item => item.ExtraData.Serialize()), second.Select(item => item.ExtraData.Serialize()));
        Assert.All(second, item => Assert.Equal(Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(first.Single(original => original.Id == item.Id)))), Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(item)))));
        var world = new WiredChestProtocolTests.World();
        var floors = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(world.Room.GetRoomItemHandler())!;
        floors.Clear();

        foreach (var item in second) {
            floors.TryAdd(item.Id, item);
        }

        var saved = second.Select(item => item.ExtraData.Serialize()).ToArray();
        var game = new Plus.HabboHotel.Rooms.Games.GameManager(world.Room, TimeProvider.System);
        game.AddPointToTeam(Plus.HabboHotel.Rooms.Games.Teams.Team.Blue, 7);
        game.Reset();
        Assert.Equal(saved, second.Select(item => item.ExtraData.Serialize()));
        fixture.Connection.Execute("UPDATE items SET extra_data='malformed history' WHERE id=500");
        Assert.Throws<ArgumentException>(() => loader.Load(42));
    }

    [WiredChestDatabaseFact]
    public void TonerCompanionLoadToggleWriteReloadAndSqlFailurePreserveExactTypedState()
    {
        using var fixture = new WiredChestDatabaseTests.Fixture();
        PrepareItems(fixture);
        fixture.Connection.Execute("CREATE TABLE room_items_toner(id INT UNSIGNED PRIMARY KEY,enabled BOOL,data1 INT,data2 INT,data3 INT); INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(100,1,42,100,'0'); INSERT INTO room_items_toner VALUES(100,TRUE,120,141,120)");
        var definition = new ItemDefinition { Id = 100, Type = ItemType.Floor, ItemName = "roombg_color", InteractionType = InteractionType.Toner, Width = 1, Length = 1 };
        var definitions = new TestWiredDefinitions(() => new() { [100] = definition });
        var metadata = new RoomItemMetadataStore(fixture.Database);
        var world = new WiredChestProtocolTests.World(fixture.Database);
        world.Room.Type = "private";
        world.Room.OwnerName = "owner";
        world.Habbo.Username = "owner";
        world.Room.UsersWithRights = [];
        Set(world.Room, "_interactionClock", world.Clock);
        Set(world.Room, "_roomItemHandling", new RoomItemHandling(world.Room, TestRoomItemStore.Instance, metadata, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        Set(world.Room, "_gamemap", new Gamemap(world.Room, new RoomModel("toner", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, fixture.Database, TestNavigationRewards.Instance));
        world.Room.GetGameMap().GenerateMaps();
        var item = new RoomFurnitureLoader(fixture.Database, definitions).Load(42).Single(item => item.Id == 100);
        world.Room.GetRoomItemHandler().LoadFurniture([item]);
        AssertToner(item, [1, 120, 141, 120]);
        var use = new FurnitureUseService(new FurnitureUseStore(fixture.Database), null!);
        use.Use(world.Room, world.Client, new(100, 0));
        AssertToner(item, [0, 120, 141, 120]);
        var service = new RoomItemMetadataService(metadata, null!);
        service.SetToner(world.Room, world.Client, new(100, 0, 255, 17));
        AssertToner(item, [1, 0, 255, 17]);
        world.Room.TonerData = null!;
        var reloaded = new RoomFurnitureLoader(fixture.Database, definitions).Load(42).Single(item => item.Id == 100);
        world.Room.GetRoomItemHandler().LoadFurniture([reloaded]);
        AssertToner(reloaded, [1, 0, 255, 17]);
        world.Packets.Clear();
        var wire = Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(reloaded)));
        fixture.Connection.Execute("CREATE TRIGGER reject_toner BEFORE UPDATE ON room_items_toner FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced toner failure'");
        Assert.Throws<MySqlConnector.MySqlException>(() => service.SetToner(world.Room, world.Client, new(100, 77, 88, 99)));
        Assert.Throws<MySqlConnector.MySqlException>(() => use.Use(world.Room, world.Client, new(100, 0)));
        Assert.Equal(wire, Payload(new ObjectUpdateComposer(RoomItemSnapshot.Capture(reloaded))));
        Assert.Equal((1, 0, 255, 17), (world.Room.TonerData.Enabled, world.Room.TonerData.Hue, world.Room.TonerData.Saturation, world.Room.TonerData.Lightness));
        var row = fixture.Connection.QuerySingle<(int Enabled, int Hue, int Saturation, int Lightness)>("SELECT enabled,data1,data2,data3 FROM room_items_toner WHERE id=100");
        Assert.Equal((1, 0, 255, 17), row);
        Assert.Empty(world.Packets);
    }

    private static void PrepareItems(WiredChestDatabaseTests.Fixture fixture) => fixture.Connection.Execute("ALTER TABLE users ADD username VARCHAR(100) NOT NULL DEFAULT ''; ALTER TABLE items ADD x INT DEFAULT 1,ADD y INT DEFAULT 1,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT; CREATE TABLE items_groups(id INT UNSIGNED PRIMARY KEY,group_id INT)");
    private static void AssertToner(Item item, int[] expected) => Assert.Equal(expected, Assert.IsType<FurnitureDataSnapshot.Integers>(RoomItemSnapshot.Capture(item).Data).Values);

    internal static WiredChestProtocolTests.World TonerWorld()
    {
        var world = new WiredChestProtocolTests.World();
        world.Room.Type = "private";
        world.Room.OwnerName = "owner";
        world.Habbo.Username = "owner";
        world.Room.UsersWithRights = [];
        world.Chest.Definition = new() { Type = ItemType.Floor, InteractionType = InteractionType.Toner, ItemName = "roombg_color" };
        world.Chest.ExtraData = new LegacyDataFormat { Data = "0" };
        Set(world.Chest, "_room", world.Room);
        world.Room.TonerData = new(100, new() { Enabled = false, Hue = 5, Saturation = 6, Lightness = 7 });

        return world;
    }

    private sealed class Metadata : IRoomItemMetadataStore
    {
        public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) { }
        public TonerRecord? LoadToner(uint itemId) => throw new NotSupportedException();
        public void SetMannequinData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
        public void SetBrandingData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
        public Plus.HabboHotel.Items.Data.Moodlight.MoodlightRecord? LoadMoodlight(uint itemId) => throw new NotSupportedException();
        public void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled) => throw new NotSupportedException();
        public void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value) => throw new NotSupportedException();
    }

    internal static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    internal static byte[] Payload(IServerPacket composer)
    {
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        composer.Compose(new FlashOutgoingPacket(stream));

        return stream.ToArray()[6..];
    }
    internal static int ReadInt(BinaryReader reader) => System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
    internal static string ReadString(BinaryReader reader) => System.Text.Encoding.UTF8.GetString(reader.ReadBytes(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2))));
}
