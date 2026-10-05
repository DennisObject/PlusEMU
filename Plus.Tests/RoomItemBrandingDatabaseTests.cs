using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public sealed class RoomItemBrandingDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void BrandingStoreWritesOnlyTheExactRoomRowAndAcceptsAnUnchangedValue()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "room_branding_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("CREATE TABLE items (id INT PRIMARY KEY, room_id INT, extra_data TEXT); INSERT INTO items VALUES (92,42,'original'),(93,43,'other');");
            var options = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema, Pooling = false };
            var store = new RoomItemMetadataStore(new ProbeDatabase(options.ConnectionString));

            store.SetBrandingData(92, 42, "state\t0\na\t1");
            Assert.Equal("state\t0\na\t1", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=92"));

            // An identical value changes no rows; the existence contract still holds and must not fail.
            store.SetBrandingData(92, 42, "state\t0\na\t1");
            Assert.Throws<InvalidOperationException>(() => store.SetBrandingData(92, 43, "wrong-room"));
            Assert.Throws<InvalidOperationException>(() => store.SetBrandingData(94, 42, "missing-row"));

            Assert.Equal("state\t0\na\t1", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=92"));
            Assert.Equal("other", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=93"));
        }
        finally
        {
            connection.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
