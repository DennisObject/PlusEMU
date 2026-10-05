using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public sealed class ItemTravelStoreDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void TravelQueriesPreserveNoRowZeroOrderingAndExactHopperKeys()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            Database = "information_schema",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        var schema = "task_item_travel_" + Guid.NewGuid().ToString("N")[..12];
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            options.Database = schema;
            var database = new ProbeDatabase(options.ConnectionString);
            using (var connection = database.Connection())
            {
                connection.Execute("""
                    CREATE TABLE items_hopper (hopper_id INT UNSIGNED NOT NULL, room_id INT UNSIGNED NOT NULL);
                    CREATE TABLE room_items_tele_links (tele_one_id INT UNSIGNED NOT NULL, tele_two_id INT UNSIGNED NOT NULL);
                    CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, room_id INT UNSIGNED NOT NULL);
                    INSERT INTO items_hopper VALUES (300,30),(100,10),(200,20);
                    INSERT INTO room_items_tele_links VALUES (7,8);
                    INSERT INTO items VALUES (8,42);
                    """);
            }
            var store = new ItemTravelStore(database);

            Assert.Equal(10u, store.FindOtherHopperRoom(20));
            Assert.Equal(200u, store.FindHopper(20));
            Assert.Equal(8u, store.FindLinkedTeleporter(7));
            Assert.Equal(42u, store.FindItemRoom(8));
            Assert.Equal(0u, store.FindHopper(9999));
            Assert.Equal(0u, store.FindLinkedTeleporter(9999));
            Assert.Equal(0u, store.FindItemRoom(9999));

            store.RegisterHopper(400, 40);
            store.RegisterHopper(400, 41);
            store.RemoveHopper(400, 40);
            using var probe = database.Connection();
            Assert.Equal([(400u, 41u)], probe.Query<(uint, uint)>(
                "SELECT hopper_id,room_id FROM items_hopper WHERE hopper_id=400 ORDER BY room_id").ToArray());
            probe.Execute("DELETE FROM items_hopper; INSERT INTO items_hopper VALUES (9999,9999)");
            Assert.Equal(0u, store.FindOtherHopperRoom(9999));
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
