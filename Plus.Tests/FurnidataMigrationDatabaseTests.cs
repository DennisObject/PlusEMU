using System.Text.Json.Nodes;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

// Runs Database/Migrations/60_FurnitureFurnidata.sql with a FurnitureData.json in @furnidata on furniture rows in the
// shape they had before, then generates furnidata from the result.
public sealed class FurnidataMigrationDatabaseTests
{
    private static string Migration => File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/60_FurnitureFurnidata.sql"));

    private static JsonObject Entry(int id, string classname, bool wall = false, string? name = null, JsonArray? colors = null)
    {
        var entry = new JsonObject
        {
            ["id"] = id,
            ["classname"] = classname,
            ["revision"] = id * 3,
            ["category"] = "other",
            ["name"] = name ?? classname,
            ["description"] = classname + " text",
            ["adurl"] = "",
            ["offerid"] = 5,
            ["buyout"] = true,
            ["rentofferid"] = -1,
            ["rentbuyout"] = false,
            ["bc"] = true,
            ["excludeddynamic"] = false,
            ["customparams"] = "",
            ["specialtype"] = 1,
            ["furniline"] = "line",
            ["environment"] = "",
            ["rare"] = false
        };

        if (!wall) {
            entry["defaultdir"] = 2;
            entry["xdim"] = 2;
            entry["ydim"] = 3;
            entry["canstandon"] = false;
            entry["cansiton"] = true;
            entry["canlayon"] = false;

            if (colors != null) {
                entry["partcolors"] = new JsonObject { ["color"] = colors };
            }
        }

        return entry;
    }

    [RoomComponentDatabaseFact]
    public void EntriesBecomeTheirRowsAndGenerateTheSameFurnidata()
    {
        var chair = Entry(10, "chair", colors: ["#fff", "#0"]);
        chair["adurl"] = null;
        chair["environment"] = null;
        chair["height"] = 0.9;
        chair["tradeable"] = false;
        var floor = new JsonArray(chair, Entry(20, "lamp"), Entry(51, "table", colors: []), Entry(70, "rug", name: "Rūg’s ´"));
        var wall = new JsonArray(Entry(30, "Poster", wall: true));
        var furnidata = new JsonObject { ["roomitemtypes"] = new JsonObject { ["furnitype"] = floor }, ["wallitemtypes"] = new JsonObject { ["furnitype"] = wall } };

        InSchema(connection =>
        {
            connection.Execute("""
                INSERT INTO furniture (id, item_name, type, sprite_id) VALUES
                    (1, 'chair', 's', 10), (2, 'chair', 's', 10), (3, 'lamp', 's', 99), (4, 'Poster ', 'i', 30),
                    (5, 'table', 's', 50), (6, 'table', 's', 51), (7, 'rug', 'i', 70), (8, 'nothing', 's', 60), (9, 'effect', 'e', 10)
                """);
            connection.Execute("SET @furnidata = @json", new { json = furnidata.ToJsonString() });
            connection.Execute(Migration);

            // The exact classname of the same kind, its sprite id first, then the oldest row; the sprite id becomes the entry id.
            Assert.Equal([(1u, 10), (3u, 20), (6u, 51)], connection.Query<(uint, int)>("SELECT id, sprite_id FROM furniture WHERE has_furnidata AND id < 10 ORDER BY id"));
            // Entries no row has get a row: 'Poster ' and the wall rug are other furni.
            var added = connection.Query<(string, string, int, int, int, string)>(
                "SELECT item_name, type, sprite_id, width, length, public_name FROM furniture WHERE id > 9 ORDER BY id").ToList();
            Assert.Equal([("rug", "s", 70, 2, 3, "R?g?s ´"), ("Poster", "i", 30, 1, 1, "Poster")], added);
            Assert.Equal(("#fff,#0", null, ""), connection.QuerySingle<(string?, string?, string?)>(
                "SELECT (SELECT part_colors FROM furniture WHERE id = 1), (SELECT part_colors FROM furniture WHERE id = 3), (SELECT part_colors FROM furniture WHERE id = 6)"));

            var generated = JsonNode.Parse(CatalogFurnidata.Generate(new FurnidataRepository(connection).All(), []).Content)!;

            foreach (var section in new[] { "roomitemtypes", "wallitemtypes" }) {
                var expected = furnidata[section]!["furnitype"]!.AsArray().Select(entry => entry!.AsObject()).OrderBy(entry => (int)entry["id"]!).ToList();
                var actual = generated[section]!["furnitype"]!.AsArray().Select(entry => entry!.AsObject()).ToList();
                Assert.Equal(expected.Count, actual.Count);
                Assert.All(expected.Zip(actual), pair => Assert.True(FurnidataEntry.SameDefinition(pair.First, pair.Second), pair.Second.ToJsonString()));
            }

            // Running it again changes nothing.
            connection.Execute("SET @furnidata = @json", new { json = furnidata.ToJsonString() });
            connection.Execute(Migration);
            Assert.Equal(11, connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture"));
            Assert.Equal(5, connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture WHERE has_furnidata"));
        });
    }

    [RoomComponentDatabaseFact]
    public void WithoutFurnidataOnlyTheColumnsAreAdded()
    {
        InSchema(connection =>
        {
            connection.Execute("INSERT INTO furniture (id, item_name, type, sprite_id) VALUES (1, 'chair', 's', 10)");
            connection.Execute(Migration);

            Assert.Equal((false, 1), connection.QuerySingle<(bool, int)>("SELECT has_furnidata, (SELECT COUNT(*) FROM furniture) FROM furniture"));
            Assert.Empty(new FurnidataRepository(connection).All());
        });
    }

    // Each test gets its own schema with furniture as it was before migration 60.
    private static void InSchema(Action<MySqlConnection> run)
    {
        var options = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!) { AllowUserVariables = true };
        using var admin = new MySqlConnection(options.ConnectionString);
        admin.Open();
        var schema = "task_furnidata_migration_" + Guid.NewGuid().ToString("N");
        admin.Execute($"CREATE DATABASE `{schema}`");

        try {
            options.Database = schema;
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            connection.Execute("""
                CREATE TABLE furniture (id INT(11) UNSIGNED NOT NULL AUTO_INCREMENT, item_name VARCHAR(70) NOT NULL, public_name VARCHAR(56) NOT NULL DEFAULT '',
                    type ENUM('s','i','e','h','v','r','b','p') NOT NULL DEFAULT 's', width INT(11) NOT NULL DEFAULT 1, length INT(11) NOT NULL DEFAULT 1,
                    can_sit TINYINT(1) NOT NULL DEFAULT 0, is_walkable TINYINT(1) NOT NULL DEFAULT 0, sprite_id INT(11) NOT NULL DEFAULT 0,
                    PRIMARY KEY (id), KEY sprite_id (sprite_id)) ENGINE=InnoDB DEFAULT CHARSET=latin1 COLLATE=latin1_swedish_ci;
                """);
            run(connection);
        }
        finally {
            admin.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
