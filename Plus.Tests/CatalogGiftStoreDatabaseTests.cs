using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogGiftStoreDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void GiftRowsCommitTogetherAndRollbackTogether()
    {
        var connectionString = Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!;
        var schema = "task_gift_" + Guid.NewGuid().ToString("N");
        using var server = new MySqlConnection(connectionString);
        server.Open();
        server.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            using var connection = new MySqlConnection(
                new MySqlConnectionStringBuilder(connectionString) { Database = schema }.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE items (
                    id INT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
                    base_item INT UNSIGNED NOT NULL,
                    user_id INT NOT NULL,
                    extra_data TEXT NOT NULL);
                CREATE TABLE user_presents (
                    item_id INT UNSIGNED NOT NULL PRIMARY KEY,
                    base_id INT UNSIGNED NOT NULL,
                    extra_data TEXT NOT NULL);
                """);
            var store = new CatalogGiftStore();
            var present = new ItemDefinition
            {
                Id = 60,
                Type = ItemType.Floor,
                InteractionType = InteractionType.Gift
            };
            var content = new ItemDefinition { Id = 50, Type = ItemType.Floor };

            using (var rollback = connection.BeginTransaction())
            {
                store.Create(connection, rollback, 2, present, content, "wrapped", "inside");
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items", transaction: rollback));
                Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents", transaction: rollback));
                rollback.Rollback();
            }
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM items"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM user_presents"));

            using (var commit = connection.BeginTransaction())
            {
                var gift = store.Create(connection, commit, 2, present, content, "wrapped", "inside");
                commit.Commit();
                Assert.Equal(gift.Id, connection.ExecuteScalar<uint>("SELECT item_id FROM user_presents"));
            }
            Assert.Equal((2, 60u, "wrapped"), connection.QuerySingle<(int UserId, uint BaseItem, string ExtraData)>(
                "SELECT user_id, base_item, extra_data FROM items"));
            Assert.Equal((50u, "inside"), connection.QuerySingle<(uint BaseId, string ExtraData)>(
                "SELECT base_id, extra_data FROM user_presents"));
        }
        finally
        {
            server.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
