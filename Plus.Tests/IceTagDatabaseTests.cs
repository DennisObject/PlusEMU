using Dapper;
using MySqlConnector;
using Xunit;

namespace Plus.Tests;

public sealed class IceTagDatabaseFactAttribute : FactAttribute
{
    public IceTagDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ICE_TAG_DATABASE") is null) {
            Skip = "Opt-in isolated Ice Tag furniture migration check.";
        }
    }
}

public sealed class IceTagDatabaseTests
{
    [IceTagDatabaseFact]
    public void PoleMigrationIsIdempotentAndPreservesCustomFurnitureAndIds()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ICE_TAG_DATABASE"));
        connection.Open();
        var schema = "task_ice_tag_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");

        try {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            var start = pristine.IndexOf("CREATE TABLE `furniture` (", StringComparison.Ordinal);
            Assert.True(start >= 0);
            connection.Execute(pristine[start..(pristine.IndexOf(';', start) + 1)]);
            connection.Execute("""
                INSERT INTO furniture(id,item_name,sprite_id,interaction_type,stack_height) VALUES
                (99023,'es_tagging',3741,'default',3),
                (200,'es_tagging',3741,'gate',7),
                (201,'es_tagging',3742,'default',8),
                (202,'custom_pole',3741,'default',9),
                (99020,'es_skating_ice',3736,'iceskates',0.01);
                """);
            var before = connection.Query<(uint Id, string Name, int Sprite, double Height)>("SELECT id,item_name,sprite_id,stack_height FROM furniture ORDER BY id").ToArray();
            var script = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/49_IceTagPole.sql"));
            Assert.Equal(1, connection.Execute(script));
            Assert.Equal(0, connection.Execute(script));
            Assert.Equal(before, connection.Query<(uint Id, string Name, int Sprite, double Height)>("SELECT id,item_name,sprite_id,stack_height FROM furniture ORDER BY id").ToArray());
            Assert.Equal(new[] { "gate", "default", "default", "iceskates", "icetag_pole" },
                connection.Query<string>("SELECT interaction_type FROM furniture ORDER BY id").ToArray());
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
