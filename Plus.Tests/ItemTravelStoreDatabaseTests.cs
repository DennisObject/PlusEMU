using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel;
using Plus.HabboHotel.Permissions;
using Plus.Core.FigureData;
using System.Reflection;
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

        try {
            options.Database = schema;
            var database = new ProbeDatabase(options.ConnectionString);

            using (var connection = database.Connection()) {
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
            probe.Execute("""
                CREATE TRIGGER reject_hopper_insert BEFORE INSERT ON items_hopper FOR EACH ROW
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced hopper insert failure';
                CREATE TRIGGER reject_hopper_delete BEFORE DELETE ON items_hopper FOR EACH ROW
                    SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced hopper delete failure';
                """);
            Assert.Throws<MySqlException>(() => store.RegisterHopper(500, 50));
            Assert.Throws<MySqlException>(() => store.RemoveHopper(9999, 9999));
            Assert.Equal([(9999u, 9999u)], probe.Query<(uint, uint)>(
                "SELECT hopper_id,room_id FROM items_hopper").ToArray());
        }
        finally {
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public void MannequinDatabaseFailureLeavesLiveProfileAndPacketsUntouched()
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"))
        {
            Database = "information_schema",
            Pooling = false,
            AllowZeroDateTime = true,
            ConvertZeroDateTime = true
        };
        var schema = "task_mannequin_" + Guid.NewGuid().ToString("N")[..12];
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            var database = new ProbeDatabase(options.ConnectionString);

            using (var connection = database.Connection()) {
                connection.Execute("""
                    CREATE TABLE users (id INT PRIMARY KEY, look VARCHAR(200), gender VARCHAR(10));
                    INSERT INTO users VALUES (7,'old-look','M');
                    CREATE TRIGGER reject_mannequin BEFORE UPDATE ON users FOR EACH ROW
                        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='forced mannequin failure';
                    """);
            }

            var habbo = new Habbo { Id = 7, Look = "old-look", Gender = "M", Clothing = new(), Access = UserAccess.Empty };
            var (session, sent) = HabbiconTestSupport.Client(habbo);
            var figures = DispatchProxy.Create<IFigureDataManager, FigureProxy>();
            var profiles = new UserProfileService(figures, null!, null!, null!, database, TimeProvider.System,
                null!, null!, new AccountSessionGate());

            Assert.Throws<MySqlException>(() => profiles.ApplyMannequin(session, new("F", "new-look")));

            Assert.Equal(("old-look", "M"), (habbo.Look, habbo.Gender));
            Assert.Empty(sent);
            using var probe = database.Connection();
            Assert.Equal(("old-look", "M"), probe.QuerySingle<(string, string)>("SELECT look,gender FROM users WHERE id=7"));
        }
        finally {
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    public class FigureProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == "ProcessFigure" ? args![0] : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
