using System.Collections.Concurrent;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class MoodlightServiceTests
{
    [Fact]
    public async Task UpdateHandlerDecodesTheCompleteRequestBeforeDelegating()
    {
        var service = new RecordingService();
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var packet = HabbiconTestSupport.Incoming(3, 2, "#0053F7", 199);

        await new Plus.Communication.Packets.Incoming.Rooms.Furni.Moodlight.MoodlightUpdateEvent(service).Parse(room, client, packet);

        Assert.Equal(new MoodlightPresetUpdate(3, "#0053F7", 199, 2), service.Update);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
    }

    [Fact]
    public void UpdatePersistsPreparedValuesBeforePublishingAndFailureLeavesStateUntouched()
    {
        var (room, client, item, sent) = Context();
        var original = item.LegacyDataString;
        var store = new RecordingStore(() =>
        {
            Assert.Equal(original, item.LegacyDataString);
            Assert.Equal(1, room.MoodlightData!.CurrentPreset);
            Assert.Empty(sent);
        }) { Fail = true };

        Assert.Throws<InvalidOperationException>(() => new MoodlightService(store)
            .UpdatePreset(room, client, new(2, "#0053F7", 128, 2)));

        Assert.Equal(original, item.LegacyDataString);
        Assert.Equal(1, room.MoodlightData!.CurrentPreset);
        Assert.False(room.MoodlightData.Enabled);
        Assert.Empty(sent);
        Assert.Equal((item.Id, room.Id, 2, "#0053F7,128,1"), store.PresetWrite);
    }

    [Fact]
    public void InvalidAndStaleRequestsDoNotPersistOrPublish()
    {
        var (room, client, item, sent) = Context();
        var store = new RecordingStore();
        var service = new MoodlightService(store);

        service.UpdatePreset(room, client, new(0, "#0053F7", 1, 2));
        service.UpdatePreset(room, client, new(1, "invalid", 1, 2));
        service.UpdatePreset(room, client, new(1, "#0053F7", 256, 2));
        client.GetHabbo().CurrentRoom = null;
        service.Toggle(room, client);

        Assert.Equal(0, store.Writes);
        Assert.Equal("original", item.LegacyDataString);
        Assert.Empty(sent);
    }

    [Fact]
    public void SuccessfulUpdateUsesBackgroundThresholdAndPublishesOnce()
    {
        var (room, client, item, sent) = Context();
        var store = new RecordingStore();

        new MoodlightService(store).UpdatePreset(room, client, new(3, "#82F349", 0, 1));

        Assert.Equal((item.Id, room.Id, 3, "#82F349,0,0"), store.PresetWrite);
        Assert.True(room.MoodlightData!.Enabled);
        Assert.Equal(3, room.MoodlightData.CurrentPreset);
        Assert.False(room.MoodlightData.Presets[2].BackgroundOnly);
        Assert.Equal("2,3,1,#82F349,0", item.LegacyDataString);
        Assert.Empty(sent); // No room users are attached to this isolated room fixture.
    }

    private static (Room Room, GameClient Client, Item Item, List<(uint Header, byte[] Payload)> Sent) Context()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 42;
        room.OwnerName = "owner";
        room.Type = "private";
        room.UsersWithRights = [];
        var handling = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handling);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System));
        var item = new Item
        {
            Id = 4_000_000_000,
            RoomId = room.Id,
            OwnerId = 1,
            Definition = new() { InteractionType = InteractionType.Moodlight },
            ExtraData = new LegacyDataFormat { Data = "original" }
        };
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(handling)!)[item.Id] = item;
        room.MoodlightData = new(item.Id, new(1, false, 1, "#000000,255,0", "#000000,255,0", "#000000,255,0"));
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1, Username = "owner", CurrentRoom = room });
        return (room, client, item, sent);
    }

    private sealed class RecordingService : IMoodlightService
    {
        public MoodlightPresetUpdate? Update { get; private set; }
        public void ShowConfig(Room room, GameClient session) => throw new NotSupportedException();
        public void Toggle(Room room, GameClient session) => throw new NotSupportedException();
        public void UpdatePreset(Room room, GameClient session, MoodlightPresetUpdate request) => Update = request;
    }

    private sealed class RecordingStore(Action? beforeWrite = null) : IRoomItemMetadataStore
    {
        public bool Fail { get; init; }
        public int Writes { get; private set; }
        public (uint Item, uint Room, int Preset, string Value) PresetWrite { get; private set; }
        public void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value)
        {
            beforeWrite?.Invoke();
            Writes++;
            PresetWrite = (itemId, roomId, preset, value);
            if (Fail) throw new InvalidOperationException("forced failure");
        }
        public MoodlightRecord? LoadMoodlight(uint itemId) => throw new NotSupportedException();
        public void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled) => throw new NotSupportedException();
        public Plus.HabboHotel.Items.Data.Toner.TonerRecord? LoadToner(uint itemId) => throw new NotSupportedException();
        public void SetMannequinData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
        public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) => throw new NotSupportedException();
        public void SetBrandingData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
    }
}

public sealed class MoodlightMetadataDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void SidecarsUseUnsignedIdentityNarrowWritesAndRollback()
    {
        var root = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            Database = "",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var admin = new MySqlConnection(root.ConnectionString);
        admin.Open();
        var schema = "task_moodlight_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            var options = new MySqlConnectionStringBuilder(root.ConnectionString) { Database = schema };
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, room_id INT UNSIGNED NOT NULL);
                CREATE TABLE room_items_moodlight (id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY,item_id INT UNSIGNED NOT NULL,enabled BOOL NOT NULL,current_preset INT NOT NULL,preset_one VARCHAR(100) NOT NULL,preset_two VARCHAR(100) NOT NULL,preset_three VARCHAR(100) NOT NULL);
                CREATE TABLE room_items_toner (id INT UNSIGNED PRIMARY KEY,enabled BOOL NOT NULL,data1 INT NOT NULL,data2 INT NOT NULL,data3 INT NOT NULL);
                INSERT INTO items VALUES (4000000000,42),(4000000001,0);
                INSERT INTO room_items_moodlight VALUES (10,4000000000,FALSE,1,'#000000,255,0','malformed-two','malformed-three'),(11,4000000000,TRUE,3,'duplicate','duplicate','duplicate');
                INSERT INTO room_items_toner VALUES (4000000000,FALSE,1,2,3);
                """);
            var store = new RoomItemMetadataStore(new ProbeDatabase(options.ConnectionString));

            var loaded = store.LoadMoodlight(4_000_000_000);
            Assert.NotNull(loaded);
            Assert.Equal((uint)10, loaded!.SidecarId);
            Assert.False(loaded.Enabled);
            store.SetMoodlightEnabled(4_000_000_000, 42, false);
            store.UpdateMoodlightPreset(4_000_000_000, 42, 1, "#0053F7,7,1");
            store.UpdateMoodlightPreset(4_000_000_000, 42, 1, "#0053F7,7,1");
            Assert.Equal(2, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM room_items_moodlight WHERE item_id=4000000000"));
            Assert.Equal("malformed-two", connection.ExecuteScalar<string>("SELECT preset_two FROM room_items_moodlight WHERE id=10"));
            Assert.Equal("duplicate", connection.ExecuteScalar<string>("SELECT preset_one FROM room_items_moodlight WHERE id=11"));
            Assert.Throws<InvalidOperationException>(() => store.SetMoodlightEnabled(4_000_000_000, 43, true));
            Assert.Throws<InvalidOperationException>(() => store.SetMoodlightEnabled(3_999_999_999, 42, true));

            var created = store.LoadMoodlight(4_000_000_001);
            Assert.NotNull(created);
            Assert.False(created!.Enabled);
            var toner = store.LoadToner(4_000_000_001);
            Assert.NotNull(toner);
            Assert.False(toner!.Enabled);

            connection.Execute("CREATE TRIGGER reject_moodlight BEFORE UPDATE ON room_items_moodlight FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced';");
            Assert.ThrowsAny<Exception>(() => store.SetMoodlightEnabled(4_000_000_000, 42, false));
            Assert.True(connection.ExecuteScalar<bool>("SELECT enabled FROM room_items_moodlight WHERE id=10"));
        }
        finally
        {
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
