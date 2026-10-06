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
        var serverOptions = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!)
        {
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        using var connection = new MySqlConnection(serverOptions.ConnectionString);
        connection.Open();
        var schema = "room_branding_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("CREATE TABLE items (id INT UNSIGNED PRIMARY KEY, room_id INT UNSIGNED, extra_data TEXT); INSERT INTO items VALUES (92,42,'original'),(93,43,'other'),(2147483648,42,'unsigned');");
            var options = new MySqlConnectionStringBuilder(connection.ConnectionString) { Database = schema, Pooling = false };
            var store = new RoomItemMetadataStore(new ProbeDatabase(options.ConnectionString));

            store.SetBrandingData(2147483648u, 42, "unsigned-saved");
            Assert.Equal("unsigned-saved", connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=2147483648"));

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
