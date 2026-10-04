using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomComponentDatabaseFactAttribute : FactAttribute
{
    public RoomComponentDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE") is null)
            Skip = "Opt-in isolated room component MariaDB probe.";
    }
}

public sealed class RoomComponentDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void NativeBoolBotAndPetRowsMaterializeThroughComponentQueries()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "room_component_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE bots (
                    id INT PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL, name VARCHAR(50) NOT NULL,
                    motto VARCHAR(50) NOT NULL DEFAULT '', look VARCHAR(100) NOT NULL DEFAULT '', x INT NOT NULL, y INT NOT NULL,
                    z DOUBLE NOT NULL, rotation INT NOT NULL DEFAULT 0, ai_type VARCHAR(20) NOT NULL, walk_mode VARCHAR(20) NOT NULL,
                    automatic_chat BOOL NOT NULL, speaking_interval INT NOT NULL, mix_sentences BOOL NOT NULL, chat_bubble INT NOT NULL);
                CREATE TABLE bots_speech (bot_id INT NOT NULL, text VARCHAR(255) NOT NULL);
                CREATE TABLE bots_petdata (
                    id INT PRIMARY KEY, type INT NOT NULL, race VARCHAR(20) NOT NULL, color VARCHAR(20) NOT NULL,
                    experience INT NOT NULL, energy INT NOT NULL, nutrition INT NOT NULL, respect INT NOT NULL,
                    createstamp DOUBLE NOT NULL, have_saddle INT NOT NULL, anyone_ride INT NOT NULL,
                    hairdye INT NOT NULL, pethair INT NOT NULL, gnome_clothing VARCHAR(100) NOT NULL);
                CREATE TABLE room_promotions (
                    room_id INT UNSIGNED NOT NULL, title VARCHAR(100) NOT NULL, description VARCHAR(255) NOT NULL,
                    timestamp_start DOUBLE NOT NULL, timestamp_expire DOUBLE NOT NULL, category_id INT NOT NULL);
                CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, user_id INT NOT NULL, room_id INT UNSIGNED NOT NULL DEFAULT 0,
                    x INT NOT NULL DEFAULT 0, y INT NOT NULL DEFAULT 0, z DOUBLE NOT NULL DEFAULT 0, rot INT NOT NULL DEFAULT 0,
                    extra_data TEXT, wall_pos VARCHAR(100), base_item INT UNSIGNED NOT NULL DEFAULT 0,
                    limited_number INT UNSIGNED NOT NULL DEFAULT 0, limited_stack INT UNSIGNED NOT NULL DEFAULT 0);
                CREATE TABLE items_groups (id INT UNSIGNED PRIMARY KEY, group_id INT NOT NULL);
                CREATE TABLE users (id INT PRIMARY KEY, username VARCHAR(100));
                CREATE TABLE logs_client_trade (
                    id INT AUTO_INCREMENT PRIMARY KEY, `1id` INT, `2id` INT, `1items` TEXT, `2items` TEXT, `timestamp` CHAR(20));
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY, users_now INT NOT NULL DEFAULT 0);
                CREATE TABLE user_roomvisits (room_id INT UNSIGNED, user_id INT, exit_timestamp DOUBLE);
                """);
            connection.Execute("""
                INSERT INTO bots VALUES
                    (10, 7, 42, 'guide', 'hello', 'hd-180-1', 1, 2, 0, 3, 'generic', 'freeroam', TRUE, 12, TRUE, 5),
                    (11, 8, 42, 'pet', '', '', 4, 5, 1.5, 0, 'pet', 'freeroam', FALSE, 0, FALSE, 0),
                    (12, 9, 99, 'other', '', '', 0, 0, 0, 0, 'generic', 'freeroam', FALSE, 1, FALSE, 0);
                INSERT INTO bots_speech VALUES (10, 'first'), (10, 'second');
                INSERT INTO bots_petdata VALUES (11, 2, '3', 'ffffff', 4, 5, 6, 7, 8.5, 1, 0, 9, 10, 'hat');
                INSERT INTO room_promotions VALUES (42, 'Featured', 'Actual row', UNIX_TIMESTAMP() - 10, UNIX_TIMESTAMP() + 600, 3);
                INSERT INTO items (id, user_id) VALUES (90, 1), (91, 1);
                INSERT INTO users VALUES (7, 'owner');
                INSERT INTO items (id, user_id, room_id, x, y, z, rot, extra_data, wall_pos, base_item, limited_number, limited_stack)
                    VALUES (92, 7, 42, 2, 3, 2, 4, '100;1', '', 500, 6, 7), (93, 7, 42, 0, 0, 0, 0, '', '', 999, 0, 0);
                INSERT INTO items_groups VALUES (92, 123);
                INSERT INTO rooms VALUES (42, 0);
                INSERT INTO user_roomvisits VALUES (42, 7, 0);
                """);

            var bot = Assert.Single(RoomBotsComponent.Load(connection, 42));
            Assert.True(bot.AutomaticChat);
            Assert.True(bot.MixSentences);
            Assert.Equal(["first", "second"], RoomBotsComponent.LoadSpeech(connection, bot.Id));
            var pet = Assert.Single(RoomPetsComponent.Load(connection, 42));
            var data = Assert.IsType<RoomPetsComponent.PetData>(RoomPetsComponent.LoadData(connection, pet.Id));
            Assert.Equal((11, 42u, 1.5), (pet.Id, pet.RoomId, pet.Z));
            Assert.Equal((2, "3", "hat"), (data.Type, data.Race, data.GnomeClothing));
            var databaseConnection = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema }.ConnectionString;
            var promotion = Assert.IsType<RoomPromotion>(RoomPromotionLoader.Load(new ProbeDatabase(databaseConnection), 42));
            Assert.Equal(("Featured", "Actual row", 3), (promotion.Name, promotion.Description, promotion.CategoryId));
            var tradeStore = (ITradeStore)new RoomTradingComponent(new ProbeDatabase(databaseConnection));
            tradeStore.TransferItem(90, 2);
            tradeStore.DeleteItem(91);
            tradeStore.Log(1, 2, "90;", "91;");
            Assert.Equal(2, connection.QuerySingle<int>("SELECT user_id FROM items WHERE id = 90"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id = 91"));
            Assert.Equal((1, 2, "90;", "91;"), connection.QuerySingle<(int, int, string, string)>(
                "SELECT `1id`, `2id`, `1items`, `2items` FROM logs_client_trade"));
            var itemStore = new RoomItemStore(new ProbeDatabase(databaseConnection));
            itemStore.PlaceFloor(90, 42, 3, 4, 1.25, 2);
            itemStore.SaveMoved([new(90, 5, 6, 2.5, 4, "state", null, false)]);
            Assert.Equal((42u, 5, 6, 2.5, 4, "state"), connection.QuerySingle<(uint, int, int, double, int, string)>(
                "SELECT room_id, x, y, z, rot, extra_data FROM items WHERE id = 90"));
            Assert.Throws<InvalidOperationException>(() => new RoomItemStore(new FailingDatabase()).PlaceFloor(90, 1, 0, 0, 0, 0));
            var userStore = new RoomUserStore(new ProbeDatabase(databaseConnection));
            userStore.UpdateUserCount(42, 7);
            userStore.SaveBot(new(10, 8, 9, 1.5, "updated", "look", 4));
            userStore.SavePet(new(11, 8, 42, "pet", 2, "3", "ffffff", 8.5, 6, 7, 2.25, 40, 50, 60, 70, false));
            userStore.SavePet(new(13, 9, 42, "inserted", 4, "5", "000000", 9.5, 0, 0, 0, 0, 100, 0, 0, true));
            userStore.RecordExit(42, 7, 1234, 6);
            Assert.Equal(6, connection.QuerySingle<int>("SELECT users_now FROM rooms WHERE id = 42"));
            Assert.Equal(1234, connection.QuerySingle<double>("SELECT exit_timestamp FROM user_roomvisits WHERE room_id = 42 AND user_id = 7"));
            Assert.Equal((8, 9, 1.5, "updated", "look", 4), connection.QuerySingle<(int, int, double, string, string, int)>(
                "SELECT x, y, z, name, look, rotation FROM bots WHERE id = 10"));
            Assert.Equal((6, 7, 2.25), connection.QuerySingle<(int, int, double)>("SELECT x, y, z FROM bots WHERE id = 11"));
            Assert.Equal((40, 50, 60, 70), connection.QuerySingle<(int, int, int, int)>(
                "SELECT experience, energy, nutrition, respect FROM bots_petdata WHERE id = 11"));
            Assert.Equal("pet", connection.QuerySingle<string>("SELECT ai_type FROM bots WHERE id = 13"));
            Assert.Throws<InvalidOperationException>(() => new RoomUserStore(new FailingDatabase()).UpdateUserCount(42, 0));
            var definition = new Plus.HabboHotel.Items.ItemDefinition
            {
                Id = 500, Type = Plus.HabboHotel.Users.Inventory.Furniture.ItemType.Floor,
                InteractionType = Plus.HabboHotel.Items.InteractionType.WalkMagicTile
            };
            var loadedFurniture = new RoomFurnitureLoader(new ProbeDatabase(databaseConnection), new TestItemDataManager(definition)).Load(42);
            var loadedItem = Assert.Single(loadedFurniture);
            Assert.Equal((92u, 7u, "owner", 123, 2, 3, 2d, 4, 6u, 7u),
                (loadedItem.Id, loadedItem.OwnerId, loadedItem.Username, loadedItem.GroupId, loadedItem.GetX, loadedItem.GetY, loadedItem.GetZ,
                    loadedItem.Rotation, loadedItem.UniqueNumber, loadedItem.UniqueSeries));
            Assert.Equal("200;1", loadedItem.LegacyDataString);
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : Plus.Database.IDatabase
    {
        public bool IsConnected() => true;
        [Obsolete] public Plus.Database.Interfaces.IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public System.Data.IDbConnection Connection() => new MySqlConnection(connectionString);
    }

    private sealed class FailingDatabase : Plus.Database.IDatabase
    {
        public bool IsConnected() => false;
        [Obsolete] public Plus.Database.Interfaces.IQueryAdapter GetQueryReactor() => throw new NotSupportedException();
        public System.Data.IDbConnection Connection() => throw new InvalidOperationException("injected persistence failure");
    }

    private sealed class TestItemDataManager(Plus.HabboHotel.Items.ItemDefinition definition) : Plus.HabboHotel.Items.IItemDataManager
    {
        public void Init() { }
        public Plus.HabboHotel.Items.ItemDefinition GetItemByName(string name) => definition;
        public Dictionary<int, uint> Gifts { get; } = [];
        public Dictionary<uint, Plus.HabboHotel.Items.ItemDefinition> Items { get; } = new() { [definition.Id] = definition };
    }
}
