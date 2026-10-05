using System.Data;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredResetDatabaseFactAttribute : FactAttribute
{
    public const string Variable = "PLUS_WIRED_RESET_TEST_DB";

    public WiredResetDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a disposable database with the base schema and Wired migrations 14, 15 and 17.";
    }
}

/// <summary>What a picked-up box leaves behind in the database, against real InnoDB tables.</summary>
public class WiredResetDatabaseTests
{
    private static readonly string ConnectionString = Environment.GetEnvironmentVariable(WiredResetDatabaseFactAttribute.Variable) ?? "";

    [WiredResetDatabaseFact]
    public void ResetForgetsWhatWasSavedForTheBoxesAndNothingElse()
    {
        using var admin = Open();
        var world = Seed(admin);

        new WiredConfigurationStore(new TestDatabase()).Reset([world.Definition, world.Legacy]);

        Assert.Equal(0, Count(admin, "wired_item_configurations", "item_id", world.Definition));
        Assert.Equal(0, Count(admin, "wired_items", "id", world.Definition));
        Assert.Equal(0, Count(admin, "wired_items", "id", world.Legacy));
        Assert.Equal(0, Count(admin, "wired_reward_state", "item_id", world.Definition));
        Assert.Equal(0, Count(admin, "wired_variable_values", "definition_id", world.Definition));
        // The same box placed again defines its variable afresh, so the definition is not retired.
        Assert.False(admin.QuerySingle<bool>("SELECT retired FROM wired_variable_locks WHERE definition_id=@Id", new { Id = world.Definition }));
        // Other variables keep what the boxes hold for them, and other boxes keep their selections.
        Assert.Equal(2, Count(admin, "wired_variable_values", "definition_id", world.Other));
        Assert.Equal(1, Count(admin, "wired_item_configurations", "item_id", world.Other));
        Assert.Equal(world.Definition.ToString(), admin.QuerySingle<string>("SELECT items FROM wired_items WHERE id=@Id", new { Id = world.Selector }));
    }

    [WiredResetDatabaseFact]
    public void AFailedResetChangesNothing()
    {
        using var admin = Open();
        var world = Seed(admin);
        var trigger = "wired_reset_probe_" + Guid.NewGuid().ToString("N")[..12];
        // The last statement fails, after every other row was already deleted in the transaction.
        admin.Execute($"CREATE TRIGGER `{trigger}` BEFORE DELETE ON wired_reward_state FOR EACH ROW BEGIN IF OLD.item_id={world.Definition} THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='Disposable Wired reset rollback probe'; END IF; END");
        try
        {
            Assert.Throws<MySqlException>(() => new WiredConfigurationStore(new TestDatabase()).Reset([world.Definition, world.Legacy]));
        }
        finally
        {
            admin.Execute($"DROP TRIGGER IF EXISTS `{trigger}`");
        }

        Assert.Equal(1, Count(admin, "wired_item_configurations", "item_id", world.Definition));
        Assert.Equal(1, Count(admin, "wired_items", "id", world.Definition));
        Assert.Equal(1, Count(admin, "wired_items", "id", world.Legacy));
        Assert.Equal(1, Count(admin, "wired_reward_state", "item_id", world.Definition));
        Assert.Equal(2, Count(admin, "wired_variable_values", "definition_id", world.Definition));
    }

    [WiredResetDatabaseFact]
    public void ABoxPlacedAgainStartsFromDefaultsAndCanBeSavedAndWritten()
    {
        using var admin = Open();
        var (room, item) = PlacedVariable(admin);
        var database = new TestDatabase();
        var before = new WiredRoomVariables(room, database, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        Save(before, item, 7);
        Assert.True(before.Module.SaveGlobalValue(item.Id, 42));
        Assert.Equal(42, GlobalValue(admin, item.Id));

        new WiredConfigurationStore(database).Reset([item.Id]);

        // A writer still holding the old definition is turned away and cannot bring the value back.
        Assert.False(before.Module.SaveGlobalValue(item.Id, 50));
        Assert.Equal(0, Count(admin, "wired_variable_values", "definition_id", item.Id));

        Assert.Null(new WiredConfigurationStore(database).Load(item.Id, item.Definition.WiredDescriptor!));
        var after = new WiredRoomVariables(room, database, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(2000)));
        Save(after, item, 3);
        Assert.Equal(3, GlobalValue(admin, item.Id));
        Assert.True(after.Module.SaveGlobalValue(item.Id, 5));
        Assert.Equal(5, GlobalValue(admin, item.Id));
    }

    [WiredResetDatabaseFact]
    public void ResetWaitsForAVariableWriteInProgressAndClearsIt()
    {
        using var admin = Open();
        var world = Seed(admin);
        using var writer = Open();
        using var transaction = writer.BeginTransaction(IsolationLevel.ReadCommitted);
        // A value writer has authorized against the box, exactly as the variable store does, and not yet written.
        writer.Execute("SELECT room_id FROM items WHERE id=@Id FOR UPDATE", new { Id = world.Definition }, transaction);
        writer.Execute("SELECT configuration FROM wired_item_configurations WHERE item_id=@Id FOR UPDATE", new { Id = world.Definition }, transaction);
        writer.Execute("SELECT retired FROM wired_variable_locks WHERE definition_id=@Id FOR UPDATE", new { Id = world.Definition }, transaction);

        var reset = Task.Run(() => new WiredConfigurationStore(new TestDatabase()).Reset([world.Definition]));
        Assert.False(reset.Wait(TimeSpan.FromMilliseconds(500)));
        writer.Execute("INSERT INTO wired_variable_values(definition_id,target_kind,holder_id,value,created_at,updated_at) VALUES (@Id,0,99,11,'1970-01-01 00:00:00.001000','1970-01-01 00:00:00.001000')",
            new { Id = world.Definition }, transaction);
        transaction.Commit();

        Assert.True(reset.Wait(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, Count(admin, "wired_variable_values", "definition_id", world.Definition));
    }

    private static void Save(WiredRoomVariables variables, Item item, int initial)
    {
        var box = Assert.IsType<WiredVariableDefinitionBox>(variables.CreateBox(item));
        Assert.False(box.HasPersistedConfiguration);
        Assert.True(box.TryValidateConfiguration(new WiredConfiguration { IntParams = [10, initial], Text = "reset_probe" }, out var validated, out _));
        box.PersistConfiguration(validated);
        box.ApplyConfiguration(validated);
        variables.ConfigurationSaved(box);
    }

    private sealed record World(uint Definition, uint Legacy, uint Other, uint Selector);

    /// <summary>
    /// A room variable box with every kind of saved row, a legacy box, another variable holding values on both, and a
    /// box selecting the variable box.
    /// </summary>
    private static World Seed(MySqlConnection admin)
    {
        var (room, definition) = PlacedVariable(admin);
        var legacy = PlaceItem(admin, (uint)room.OwnerId, room.Id);
        var other = PlaceItem(admin, (uint)room.OwnerId, room.Id);
        var selector = PlaceItem(admin, (uint)room.OwnerId, room.Id);
        Configure(admin, definition.Id, "wf_var_room", new WiredConfiguration { IntParams = [10, 7], Text = "reset_probe" });
        Configure(admin, other, "wf_var_furni", new WiredConfiguration { IntParams = [1, 10], Text = "reset_other" });
        admin.Execute("INSERT INTO wired_variable_locks(definition_id) VALUES (@Definition),(@Other)", new { Definition = definition.Id, Other = other });
        admin.Execute("""
            INSERT INTO wired_variable_values(definition_id,target_kind,holder_id,value,created_at,updated_at) VALUES
            (@Definition,3,0,42,@At,@At),(@Definition,0,@Owner,5,@At,@At),(@Other,1,@Definition,9,@At,@At),(@Other,1,@Legacy,8,@At,@At)
            """, new { Definition = definition.Id, Other = other, Legacy = legacy, Owner = room.OwnerId, At = DateTime.UnixEpoch.AddMilliseconds(1) });
        admin.Execute("INSERT INTO wired_items VALUES (@Id,'',0,'probe','0')", new { Id = definition.Id });
        admin.Execute("INSERT INTO wired_items VALUES (@Id,'',5,'legacy probe','1')", new { Id = legacy });
        admin.Execute("INSERT INTO wired_items VALUES (@Id,@Items,0,'','0')", new { Id = selector, Items = definition.Id.ToString() });
        admin.Execute("INSERT INTO wired_reward_state(item_id,claims) VALUES (@Id,'{\"1\":{}}')", new { Id = definition.Id });
        return new(definition.Id, legacy, other, selector);
    }

    private static (Room Room, Item Item) PlacedVariable(MySqlConnection admin)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var owner = Insert(admin, "users", new() { ["username"] = "wr_" + suffix, ["password"] = suffix, ["mail"] = suffix + "@invalid" });
        var roomId = Insert(admin, "rooms", new() { ["owner"] = owner.ToString(), ["caption"] = "Disposable Wired reset probe",
            ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = roomId; room.OwnerId = (int)owner; room.OwnerName = "owner"; room.Type = "private";
        var item = new Item { Id = PlaceItem(admin, owner, roomId), RoomId = roomId, OwnerId = owner,
            Definition = new() { ItemName = "wf_var_room", InteractionName = "wf_var_room", Type = ItemType.Floor } };
        return (room, item);
    }

    private static uint PlaceItem(MySqlConnection admin, uint owner, uint roomId) => Insert(admin, "items", new()
    {
        ["user_id"] = owner, ["room_id"] = roomId, ["base_item"] = admin.QueryFirst<uint>("SELECT id FROM furniture LIMIT 1"),
        ["extra_data"] = "", ["wall_pos"] = ""
    });

    private static void Configure(MySqlConnection admin, uint itemId, string name, WiredConfiguration configuration) =>
        admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@Id,@Name,1,@Json)",
            new { Id = itemId, Name = name, Json = JsonSerializer.Serialize(configuration) });

    private static int GlobalValue(MySqlConnection admin, uint definitionId) =>
        admin.QuerySingle<int>("SELECT value FROM wired_variable_values WHERE definition_id=@Id AND target_kind=3 AND holder_id=0", new { Id = definitionId });

    private static int Count(MySqlConnection admin, string table, string column, uint id) =>
        admin.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{table}` WHERE `{column}`=@Id", new { Id = id });

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

    private static MySqlConnection Open()
    {
        var connection = new MySqlConnection(ConnectionString);
        connection.Open();
        return connection;
    }

    private sealed class TestDatabase : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(ConnectionString);
    }
}
