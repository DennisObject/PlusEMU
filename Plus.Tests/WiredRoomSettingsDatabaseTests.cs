using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public class WiredRoomSettingsDatabaseTests(ITestOutputHelper output)
{
    [WiredVariableDatabaseFact]
    public async Task GuardedMySqlSettingsRoutesCommitReloadRejectUnauthorizedAndRollbackFailures()
    {
        var connectionString = GuardedConnectionString();
        using var connection = new MySqlConnection(connectionString); await connection.OpenAsync();
        var migration = File.ReadAllBytes(Path.GetFullPath(Path.Join(AppContext.BaseDirectory, "../../../../Database/Migrations/18_AddWiredRoomSettings.sql")));
        Assert.Equal("9ae63937de923b92dd55a7eb27b43f7b0966ca7b48c3d74de198cb263f1641d8", Convert.ToHexString(SHA256.HashData(migration)).ToLowerInvariant());
        // The coordinator authorized only this exact additive migration in the guarded preview database.
        connection.Execute(System.Text.Encoding.UTF8.GetString(migration));
        var schema = connection.QuerySingle<dynamic>("SHOW CREATE TABLE room_wired_settings");
        output.WriteLine((string)((IDictionary<string, object>)schema)["Create Table"]);
        Assert.Equal(new[] { "room_id", "inspect_mask", "modify_mask", "timezone" }, connection.Query<string>(
            "SELECT COLUMN_NAME FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='room_wired_settings' ORDER BY ORDINAL_POSITION"));
        var users = new List<uint>(); uint roomId = 0, itemId = 0, variableId = 0, userVariableId = 0;
        var trigger = "wired_settings_probe_" + Guid.NewGuid().ToString("N")[..12];
        try
        {
            var suffix = Guid.NewGuid().ToString("N")[..12];
            var ownerId = Insert(connection, "users", new() { ["username"] = "ws_owner_" + suffix, ["password"] = suffix, ["mail"] = suffix + "@invalid" }); users.Add(ownerId);
            var guestId = Insert(connection, "users", new() { ["username"] = "ws_guest_" + suffix, ["password"] = suffix, ["mail"] = suffix + "g@invalid" }); users.Add(guestId);
            roomId = Insert(connection, "rooms", new() { ["owner"] = ownerId.ToString(), ["caption"] = "Disposable Wired settings probe",
                ["model_name"] = connection.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
            var database = new PreviewDatabase(connectionString);
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = roomId; room.Name = "Settings menu probe"; room.OwnerId = (int)ownerId; room.OwnerName = "owner"; room.Type = "private"; room.UsersWithRights = [];
            var manager = new RoomUserManager(room); Set(room, "_roomUserManager", manager);
            var owner = Client(room, (int)ownerId, "owner", manager, 1);
            var guest = Client(room, (int)guestId, "guest", manager, 2);
            var settings = WiredRoomSettings.For(room, database);
            Assert.Null(settings.ExplicitTimeZone);
            await new WiredRoomSettingsRequestEvent(database).Parse(room, guest.Client, Packet());
            var initial = Assert.Single(guest.Packets).Payload;
            Assert.Equal((int)roomId, initial.ReadInt()); Assert.Equal(2, initial.ReadInt()); Assert.Equal(2, initial.ReadInt());
            Assert.False(initial.ReadBool()); Assert.False(initial.ReadBool()); Assert.False(initial.ReadBool()); Assert.Equal("", initial.ReadString());
            guest.Packets.Clear(); owner.Packets.Clear();

            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1, "Europe/Berlin"));
            Assert.Equal(new(1, 0, "Europe/Berlin"), settings.Snapshot);
            Assert.Equal(settings.Snapshot, new DatabaseWiredRoomSettingsStore(database).Load(roomId));
            Assert.Equal("Europe/Berlin", settings.ExplicitTimeZone!.Id);
            Assert.Single(owner.Packets); Assert.Single(guest.Packets);
            Assert.True(settings.CanInspect(guest.Client)); Assert.False(settings.CanModify(guest.Client));
            var saved = settings.Snapshot;
            owner.Packets.Clear(); guest.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, guest.Client, Packet(2, 2, "UTC"));
            Assert.Same(saved, settings.Snapshot); Assert.Equal(saved, new DatabaseWiredRoomSettingsStore(database).Load(roomId));
            Assert.Contains(guest.Packets, p => p.Id == 156); Assert.Empty(owner.Packets);

            owner.Packets.Clear(); guest.Packets.Clear();
            await new WiredRoomSettingsSaveEvent(database).Parse(room, owner.Client, Packet(0, 2));
            Assert.Equal(new(2, 2, "Europe/Berlin"), settings.Snapshot); // Two-int route retains timezone, modify implies inspect.
            // Inspect is allowed independently of ordinary decoration rights; explicit modify=0 denies a prior decorator.
            var itemHandler = new RoomItemHandling(room); Set(room, "_roomItemHandling", itemHandler);
            var wired = new WiredComponent(room); Set(room, "_wiredComponent", wired);
            itemId = Insert(connection, "items", new() { ["user_id"] = ownerId, ["room_id"] = roomId,
                ["base_item"] = connection.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1"), ["extra_data"] = "", ["wall_pos"] = "" });
            var item = new Item { Id = itemId, RoomId = roomId, ExtraData = new LegacyDataFormat { Data = "1" },
                Definition = new() { Type = ItemType.Floor, ItemName = "wf_act_toggle_state" } };
            Set(item, "_room", room);
            ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(itemHandler)!).TryAdd(itemId, item);
            var box = wired.CreateConfiguredBox(item)!; Assert.True(wired.AddBox(box)); var original = box.Configuration;
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1, "Europe/Berlin"));
            guest.Packets.Clear(); await new OpenWiredEvent().Parse(room, guest.Client, ItemPacket(itemId, false));
            Assert.Equal(1428u, Assert.Single(guest.Packets).Id); Assert.Same(original, box.Configuration);
            room.UsersWithRights.Add((int)guestId); Assert.True(room.CheckRights(guest.Client, false, true));
            guest.Packets.Clear(); await new SaveWiredEffectConfigEvent(database, null!).Parse(guest.Client, ItemPacket(itemId, true));
            Assert.Empty(guest.Packets); Assert.Same(original, box.Configuration);
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_item_configurations WHERE item_id=@Id", new { Id = itemId }));
            await new WiredRoomSettingsSaveEvent(database).Parse(room, owner.Client, Packet(0, 2));
            variableId = Insert(connection, "items", new() { ["user_id"] = ownerId, ["room_id"] = roomId,
                ["base_item"] = connection.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1"), ["extra_data"] = "", ["wall_pos"] = "" });
            var variableConfig = new WiredConfiguration { IntParams = [10, 7], Text = "settings_menu_probe" };
            connection.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@Id,'wf_var_room',1,@Json)",
                new { Id = variableId, Json = JsonSerializer.Serialize(variableConfig) });
            var variableItem = new Item { Id = variableId, RoomId = roomId, OwnerId = ownerId,
                Definition = new() { ItemName = "wf_var_room", InteractionName = "wf_var_room", Type = ItemType.Floor } };
            ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(itemHandler)!).TryAdd(variableId, variableItem);
            var variables = new WiredRoomVariables(room, database, () => 1234);
            Set(wired, "_variables", new Lazy<WiredRoomVariables>(() => variables));
            var definition = variables.CreateBox(variableItem)!;
            Assert.True(definition.TryValidateConfiguration(variableConfig, out variableConfig, out _));
            definition.ApplyConfiguration(variableConfig); variables.ConfigurationLoaded(definition);
            userVariableId = Insert(connection, "items", new() { ["user_id"] = ownerId, ["room_id"] = roomId,
                ["base_item"] = connection.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1"), ["extra_data"] = "", ["wall_pos"] = "" });
            var userVariableConfig = new WiredConfiguration { IntParams = [1, 10], Text = "settings_clear_probe" };
            connection.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@Id,'wf_var_user',1,@Json)",
                new { Id = userVariableId, Json = JsonSerializer.Serialize(userVariableConfig) });
            connection.Execute("INSERT INTO wired_variable_values(definition_id,target_kind,holder_id,value,created_at_ms,updated_at_ms) VALUES (@Id,0,@Holder,12,1234,1234)",
                new { Id = userVariableId, Holder = guestId });
            await AssertVariableMenuSettingsGates(room, settings, variables, owner.Client, guest.Client, guest.Packets, connection, variableId, userVariableId);
            var accepted = settings.Snapshot;
            owner.Packets.Clear(); guest.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(1, 1, "UTC"));
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1, "Invalid/Timezone"));
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1));
            Assert.Same(accepted, settings.Snapshot); Assert.Equal(accepted, new DatabaseWiredRoomSettingsStore(database).Load(roomId));

            // The real UPDATE fails inside its open transaction. No row change or permissions broadcast may follow.
            connection.Execute($"CREATE TRIGGER `{trigger}` BEFORE UPDATE ON room_wired_settings FOR EACH ROW BEGIN IF NEW.room_id={roomId} THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Disposable Wired settings rollback probe'; END IF; END");
            owner.Packets.Clear(); guest.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1, "UTC"));
            Assert.Same(accepted, settings.Snapshot); Assert.Equal(accepted, new DatabaseWiredRoomSettingsStore(database).Load(roomId));
            Assert.Equal(156u, owner.Packets[0].Id); Assert.Equal(5102u, owner.Packets[1].Id); Assert.Equal(2, owner.Packets.Count); Assert.Empty(guest.Packets);
            connection.Execute($"DROP TRIGGER `{trigger}`");

            // Ownership changed in storage while the room still has its previous owner snapshot.
            connection.Execute("UPDATE rooms SET owner=@Owner WHERE id=@Id", new { Owner = guestId.ToString(), Id = roomId });
            owner.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(0, 1, "UTC"));
            Assert.Same(accepted, settings.Snapshot); Assert.Equal(156u, owner.Packets[0].Id); Assert.Equal(5102u, owner.Packets[1].Id);
            connection.Execute("UPDATE rooms SET owner=@Owner WHERE id=@Id", new { Owner = ownerId.ToString(), Id = roomId });

            var stale = new DatabaseWiredRoomSettingsStore(database).Load(roomId);
            var store = new DatabaseWiredRoomSettingsStore(database);
            store.Save(roomId, (int)ownerId, false, stale, new(1, 0, "UTC"));
            Assert.Throws<InvalidOperationException>(() => store.Save(roomId, (int)ownerId, false, stale, new(2, 2, "UTC")));
            Assert.Equal(new(1, 0, "UTC"), store.Load(roomId));
            // The live room still holds the old expected row. Its actual client reload must recover the CAS conflict.
            owner.Packets.Clear(); guest.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(2, 2, "Europe/Berlin"));
            Assert.Same(accepted, settings.Snapshot); Assert.Equal(156u, owner.Packets[0].Id); Assert.Equal(5102u, owner.Packets[1].Id);
            Assert.Empty(guest.Packets);
            owner.Packets.Clear();
            await new WiredRoomSettingsRequestEvent(database).Parse(room, owner.Client, Packet());
            var reloaded = Assert.Single(owner.Packets).Payload;
            Assert.Equal((int)roomId, reloaded.ReadInt()); Assert.Equal(1, reloaded.ReadInt()); Assert.Equal(0, reloaded.ReadInt());
            Assert.Equal(new(1, 0, "UTC"), settings.Snapshot); Assert.Empty(guest.Packets);
            owner.Packets.Clear();
            await new WiredMenuPermissionsSaveEvent(database).Parse(room, owner.Client, Packet(2, 2, "Europe/Berlin"));
            Assert.Equal(new(2, 2, "Europe/Berlin"), settings.Snapshot); Assert.Equal(settings.Snapshot, store.Load(roomId));
            Assert.Equal(5102u, Assert.Single(owner.Packets).Id); Assert.Equal(5102u, Assert.Single(guest.Packets).Id);
            output.WriteLine("Actual 10022/10023/1936 routes, 5102 fields, guest denial, invalid input, reload, UPDATE rollback, owner-row authorization and expected-row concurrency passed.");
        }
        finally
        {
            connection.Execute($"DROP TRIGGER IF EXISTS `{trigger}`");
            if (roomId != 0)
            {
                connection.Execute("DELETE FROM room_wired_settings WHERE room_id=@Id", new { Id = roomId });
                if (itemId != 0)
                {
                    connection.Execute("DELETE FROM wired_item_configurations WHERE item_id=@Id", new { Id = itemId });
                    connection.Execute("DELETE FROM items WHERE id=@Id", new { Id = itemId });
                }
                foreach (var definitionId in new[] { variableId, userVariableId }.Where(id => id != 0))
                {
                    connection.Execute("DELETE FROM wired_variable_values WHERE definition_id=@Id", new { Id = definitionId });
                    connection.Execute("DELETE FROM wired_variable_locks WHERE definition_id=@Id", new { Id = definitionId });
                    connection.Execute("DELETE FROM wired_item_configurations WHERE item_id=@Id", new { Id = definitionId });
                    connection.Execute("DELETE FROM items WHERE id=@Id", new { Id = definitionId });
                }
                connection.Execute("DELETE FROM rooms WHERE id=@Id", new { Id = roomId });
            }
            foreach (var id in users) connection.Execute("DELETE FROM users WHERE id=@Id", new { Id = id });
        }
    }

    private static (FlashGameClient Client, List<(uint Id, FlashIncomingPacket Payload)> Packets) Client(Room room, int id, string name, RoomUserManager manager, int virtualId)
    {
        var packets = new List<(uint, FlashIncomingPacket)>();
        var client = new FlashGameClient(null!, new FlashPacketFactory())
        {
            Revision = new() { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint> { [ServerPacketHeader.WiredRoomSettingsDataComposer] = 5102,
                [ServerPacketHeader.WiredEffectConfigComposer] = 1428, [ServerPacketHeader.WiredValidationErrorComposer] = 156,
                [ServerPacketHeader.WiredAllVariablesHashComposer] = 1646, [ServerPacketHeader.WiredAllVariablesDiffComposer] = 2498,
                [ServerPacketHeader.WiredVariableHoldersComposer] = 9462, [ServerPacketHeader.WiredVariableHoldersPageComposer] = 9461,
                [ServerPacketHeader.WiredUserVariablesDataComposer] = 5103 } },
            SendCallback = args => { packets.Add(((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2)), new() { Buffer = args.MemoryBuffer[6..].ToArray() })); return true; }
        };
        client.SetHabbo(new Habbo { Id = id, Username = name, CurrentRoom = room, Client = client, Access = EditorTestSupport.Access([]) });
        var actor = new RoomUser(id, roomId: room.Id, virtualId: virtualId, room: room); Set(actor, "_mClient", client);
        ((ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!).TryAdd(virtualId, actor);
        return (client, packets);
    }
    private static async Task AssertVariableMenuSettingsGates(Room room, WiredRoomSettings settings, WiredRoomVariables variables,
        FlashGameClient owner, FlashGameClient guest, List<(uint Id, FlashIncomingPacket Payload)> packets, MySqlConnection connection, uint definitionId, uint clearDefinitionId)
    {
        var token = $"room:{definitionId}";
        Assert.NotNull(variables.Catalog().Find(token));
        room.UsersWithRights.Remove(guest.GetHabbo().Id);
        Assert.False(room.CheckRights(guest, false, true));
        Assert.True(settings.TrySave(owner, 1, 0, "Europe/Berlin", out _));
        Assert.True(settings.CanInspect(guest)); Assert.False(settings.CanModify(guest));
        Func<Task>[] reads = [
            () => new WiredAllVariablesRequestEvent().Parse(room, guest, Packet()),
            () => new WiredVariableHashesEvent().Parse(room, guest, Packet(0)),
            () => new WiredVariableHoldersRequestEvent().Parse(room, guest, Packet(token)),
            () => new WiredVariableHoldersPageEvent().Parse(room, guest, Packet(token, 1, 15, 0, -1)),
            () => new WiredUserVariablesRequestEvent().Parse(room, guest, Packet())];
        uint[] headers = [1646, 2498, 9462, 9461, 5103];
        for (var index = 0; index < reads.Length; index++)
        {
            packets.Clear(); await reads[index](); Assert.NotEmpty(packets);
            Assert.All(packets, packet => { Assert.Equal(headers[index], packet.Id); Assert.True(packet.Payload.HasDataRemaining()); });
        }
        Assert.Equal(7, Value());
        packets.Clear();
        await new WiredUserVariableUpdateEvent().Parse(room, guest, Packet(3, (int)room.Id, (int)definitionId, 99));
        await new WiredUserVariableManageEvent().Parse(room, guest, Packet(0, 3, (int)room.Id, (int)definitionId, 88));
        await new WiredUserVariableManageEvent().Parse(room, guest, Packet(2, 0, guest.GetHabbo().Id, (int)clearDefinitionId, 0));
        Assert.Empty(packets); Assert.Equal(7, Value());

        room.UsersWithRights.Add(guest.GetHabbo().Id);
        Assert.True(settings.TrySave(owner, 2, 2, "Europe/Berlin", out _));
        Assert.True(settings.CanModify(guest)); Assert.False(settings.CanManage(guest));
        packets.Clear();
        await new WiredUserVariableUpdateEvent().Parse(room, guest, Packet(3, (int)room.Id, (int)definitionId, 9));
        Assert.Equal(9, Value()); Assert.Equal(5103u, Assert.Single(packets).Id);
        packets.Clear();
        await new WiredUserVariableManageEvent().Parse(room, guest, Packet(0, 3, (int)room.Id, (int)definitionId, 10));
        Assert.Equal(10, Value()); Assert.Equal(5103u, Assert.Single(packets).Id);
        packets.Clear();
        await new WiredUserVariableManageEvent().Parse(room, guest, Packet(2, 0, guest.GetHabbo().Id, (int)clearDefinitionId, 0));
        Assert.Equal(10, Value()); Assert.Equal(5103u, Assert.Single(packets).Id); // Editing never grants offline clear.
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@Id", new { Id = clearDefinitionId }));

        Assert.True(settings.TrySave(owner, 0, 0, "Europe/Berlin", out _));
        Assert.True(room.CheckRights(guest, false, true)); Assert.False(settings.CanInspect(guest)); Assert.False(settings.CanModify(guest));
        foreach (var read in reads) { packets.Clear(); await read(); Assert.Empty(packets); }
        packets.Clear();
        await new WiredUserVariableUpdateEvent().Parse(room, guest, Packet(3, (int)room.Id, (int)definitionId, 99));
        await new WiredUserVariableManageEvent().Parse(room, guest, Packet(0, 3, (int)room.Id, (int)definitionId, 88));
        Assert.Empty(packets); Assert.Equal(10, Value());
        Assert.True(settings.CanManage(owner));
        await new WiredUserVariableManageEvent().Parse(room, owner, Packet(2, 0, guest.GetHabbo().Id, (int)clearDefinitionId, 0));
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@Id", new { Id = clearDefinitionId }));
        Assert.True(settings.TrySave(owner, 2, 2, "Europe/Berlin", out _));
        room.UsersWithRights.Remove(guest.GetHabbo().Id);
        int Value() => connection.ExecuteScalar<int>("SELECT value FROM wired_variable_values WHERE definition_id=@Id AND target_kind=3 AND holder_id=0", new { Id = definitionId });
    }
    private static FlashIncomingPacket Packet(params object[] values)
    {
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream);
        foreach (var value in values) { if (value is int number) packet.WriteInteger(number); else packet.WriteString((string)value); }
        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }
    private static FlashIncomingPacket ItemPacket(uint id, bool save)
    {
        using var stream = PlusMemoryStream.GetStream(); var packet = new FlashOutgoingPacket(stream); packet.WriteUInteger(id);
        if (save)
        {
            packet.WriteInteger(2); packet.WriteInteger(0); packet.WriteInteger(100); packet.WriteString("");
            packet.WriteInteger(0); packet.WriteInteger(0); packet.WriteInteger(0);
        }
        return new() { Buffer = stream.ToArray().AsMemory(6) };
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
    private static string GuardedConnectionString()
    {
        using var process = Process.Start(new ProcessStartInfo("docker") { ArgumentList = { "inspect", "plus-wired-preview-db-1" }, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var inspect = process.StandardOutput.ReadToEnd(); process.WaitForExit(); Assert.Equal(0, process.ExitCode);
        using var json = JsonDocument.Parse(inspect); var container = json.RootElement[0];
        Assert.Equal("plus-wired-preview", container.GetProperty("Config").GetProperty("Labels").GetProperty("com.docker.compose.project").GetString());
        var networks = container.GetProperty("NetworkSettings").GetProperty("Networks"); Assert.Single(networks.EnumerateObject());
        Assert.True(networks.TryGetProperty("plus-wired-preview_default", out var network));
        Assert.Contains(container.GetProperty("Mounts").EnumerateArray(), mount => mount.TryGetProperty("Name", out var name) && name.GetString() == "plus-wired-preview_wired-db-data" && mount.GetProperty("Destination").GetString() == "/var/lib/mysql");
        using var config = JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("WIRED_VARIABLE_PREVIEW_CONFIG")!));
        var database = config.RootElement.GetProperty("Database");
        return new MySqlConnectionStringBuilder { Server = network.GetProperty("IPAddress").GetString(), Port = 3306,
            Database = database.GetProperty("Name").GetString(), UserID = database.GetProperty("Username").GetString(), Password = database.GetProperty("Password").GetString(),
            MinimumPoolSize = 0, MaximumPoolSize = 4 }.ConnectionString;
    }
    private static uint Insert(MySqlConnection connection, string table, Dictionary<string, object> values)
    {
        var columns = connection.Query<Column>("SELECT COLUMN_NAME AS Name,DATA_TYPE AS Type,COLUMN_TYPE AS FullType FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name=@table AND IS_NULLABLE='NO' AND COLUMN_DEFAULT IS NULL AND EXTRA NOT LIKE '%auto_increment%'", new { table });
        foreach (var column in columns.Where(column => !values.ContainsKey(column.Name))) values[column.Name] = column.Type switch
        { "enum" => column.FullType.Split('\'')[1], "datetime" or "timestamp" or "date" => DateTime.UtcNow, "varchar" or "char" or "text" or "mediumtext" or "longtext" => "", _ => 0 };
        var parameters = new DynamicParameters(); foreach (var entry in values) parameters.Add(entry.Key, entry.Value);
        connection.Execute($"INSERT INTO `{table}` ({string.Join(',', values.Keys.Select(key => $"`{key}`"))}) VALUES ({string.Join(',', values.Keys.Select(key => "@" + key))})", parameters);
        return connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()");
    }
    private sealed class Column { public string Name { get; set; } = ""; public string Type { get; set; } = ""; public string FullType { get; set; } = ""; }
    private sealed class PreviewDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
