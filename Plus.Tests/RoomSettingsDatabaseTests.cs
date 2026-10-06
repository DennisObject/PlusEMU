using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
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
            var promotionStore = new RoomPromotionStore(database);
            connection.Execute("CREATE TABLE room_promotions (room_id INT PRIMARY KEY, title VARCHAR(100), description VARCHAR(255), timestamp_start DATETIME(6), timestamp_expire DATETIME(6), category_id INT)");
            var instant = new DateTimeOffset(2040,12,31,23,0,0,TimeSpan.Zero);
            var promotion = new RoomPromotion("title","details",9,instant,instant.AddHours(2),TimeProvider.System);
            promotionStore.Save(42,8,promotion);
            Assert.Equal(instant.UtcDateTime, connection.QuerySingle<DateTime>("SELECT timestamp_start FROM room_promotions"));
            Assert.Equal(instant.AddHours(2).UtcDateTime, connection.QuerySingle<DateTime>("SELECT timestamp_expire FROM room_promotions"));
            Assert.Throws<InvalidOperationException>(() => promotionStore.Save(42,7,promotion));
            Assert.Throws<InvalidOperationException>(() => promotionStore.Edit(42,7,"rejected","rejected"));
            promotionStore.Edit(42,8,"edited","more details");
            Assert.Equal("edited", connection.QuerySingle<string>("SELECT title FROM room_promotions"));
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
            connection.Execute("ALTER TABLE items ADD COLUMN base_item INT, ADD COLUMN user_id INT; CREATE TABLE user_presents (item_id INT PRIMARY KEY,base_id INT,extra_data TEXT); INSERT INTO items (id,room_id,extra_data,base_item,user_id) VALUES (94,42,'gift',100,8); INSERT INTO user_presents VALUES (94,201,'changed')");
            var gifts = new GiftStore(database);
            Assert.Throws<InvalidOperationException>(() => gifts.Open(94,8,42,100,new(200,"blue")));
            Assert.Equal(100, connection.QuerySingle<int>("SELECT base_item FROM items WHERE id=94"));
            Assert.Equal(201, connection.QuerySingle<int>("SELECT base_id FROM user_presents WHERE item_id=94"));
            connection.Execute("UPDATE user_presents SET base_id=200,extra_data='blue' WHERE item_id=94");
            gifts.Open(94,8,42,100,new(200,"blue"));
            Assert.Equal((200,0,"blue"), connection.QuerySingle<(int,int,string)>("SELECT base_item,room_id,extra_data FROM items WHERE id=94"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM user_presents WHERE item_id=94"));
            Assert.Throws<InvalidOperationException>(() => gifts.Open(94,8,42,100,new(200,"blue")));

        }
        finally { connection.Execute($"DROP DATABASE IF EXISTS `{schema}`"); }
    }
    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public IDbConnection Connection() => new MySqlConnection(connectionString);
        public bool IsConnected() => true;
    }
}
