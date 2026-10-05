using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomSettingsDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void StoresPreserveNativeEnumsBooleansAndRejectMovedItemsOrChangedOwners()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "room_settings_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE rooms (id INT PRIMARY KEY, owner VARCHAR(75), caption VARCHAR(100), description VARCHAR(255), password VARCHAR(30),
                    category INT, state ENUM('open','locked','password','invisible'), tags VARCHAR(100), users_max INT,
                    allow_pets BOOL, allow_pets_eat BOOL, room_blocking_disabled BOOL, allow_hidewall BOOL,
                    floorthick INT, wallthick INT, mute_settings BOOL, kick_settings ENUM('0','1','2'), ban_settings BOOL,
                    chat_mode INT, chat_size INT, chat_speed INT, chat_extra_flood INT, chat_hearing_distance INT, trade_settings INT);
                INSERT INTO rooms (id,owner,caption,state) VALUES (42,'7','Original','open');
                CREATE TABLE items (id INT PRIMARY KEY, room_id INT, extra_data TEXT);
                INSERT INTO items VALUES (92,42,'original');
                CREATE TABLE room_items_toner (id INT PRIMARY KEY, enabled BOOL, data1 INT, data2 INT, data3 INT);
                INSERT INTO room_items_toner VALUES (92,FALSE,0,0,0);
                """);
            var options = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema };
            var database = new ProbeDatabase(options.ConnectionString);
            var settings = new RoomSettingsStore(database);
            var request = new RoomSettingsRequest(42, "Updated", "Description", 1, "", 25, 36, ["one"],
                2, true, false, true, false, -1, 1, 1, 2, 0, 1, 2, 0, 50, 2);
            settings.Save(request, 7, RoomAccess.Doorbell);
            Assert.Equal("2", connection.QuerySingle<string>("SELECT kick_settings FROM rooms"));
            Assert.Equal("locked", connection.QuerySingle<string>("SELECT state FROM rooms"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT allow_pets FROM rooms"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT allow_pets_eat FROM rooms"));
            Assert.Equal("one", connection.QuerySingle<string>("SELECT tags FROM rooms"));
            connection.Execute("UPDATE rooms SET owner='8'");
            Assert.Throws<InvalidOperationException>(() => settings.Save(request with { Name = "Rejected" }, 7, RoomAccess.Open));
            Assert.Equal("Updated", connection.QuerySingle<string>("SELECT caption FROM rooms"));
            var metadata = new RoomItemMetadataStore(database);
            metadata.SetToner(92,42,0,255,1);
            Assert.Equal((1,0,255,1), connection.QuerySingle<(int,int,int,int)>("SELECT enabled,data1,data2,data3 FROM room_items_toner"));
            connection.Execute("INSERT INTO items VALUES (93,43,'other'); INSERT INTO room_items_toner VALUES (93,TRUE,20,30,40)");
            var furniture = new FurnitureUseStore(database);
            furniture.SetTonerEnabled(92,42,false);
            Assert.Equal(0, connection.QuerySingle<int>("SELECT enabled FROM room_items_toner WHERE id=92"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT enabled FROM room_items_toner WHERE id=93"));
            connection.Execute("UPDATE items SET room_id=0 WHERE id=92");
            Assert.Throws<InvalidOperationException>(() => furniture.SetTonerEnabled(92,42,true));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT enabled FROM room_items_toner WHERE id=92"));
            Assert.Throws<InvalidOperationException>(() => metadata.SetToner(92,42,10,20,30));
            Assert.Throws<InvalidOperationException>(() => metadata.SetMannequinData(92,42,"rejected"));
            Assert.Equal("original", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=92"));
            Assert.Equal(255, connection.QuerySingle<int>("SELECT data2 FROM room_items_toner WHERE id=92"));
        }
        finally { connection.Execute($"DROP DATABASE IF EXISTS `{schema}`"); }
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
        public IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
    }
}
