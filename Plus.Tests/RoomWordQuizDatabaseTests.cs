using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Xunit;

namespace Plus.Tests;

public sealed class RoomWordQuizDatabaseFactAttribute : FactAttribute
{
    public RoomWordQuizDatabaseFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("ROOM_WORD_QUIZ_DATABASE") == null) {
            Skip = "Opt-in isolated room word quiz permission migration.";
        }
    }
}

public sealed class RoomWordQuizDatabaseTests
{
    [RoomWordQuizDatabaseFact]
    public void CommandPermissionMigrationIsIdempotentAndPreservesCustomRolesAndMetadata()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_WORD_QUIZ_DATABASE"));
        connection.Open();
        var schema = "room_word_quiz_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try {
            connection.Execute($"USE `{schema}`");
            var pristine = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            foreach (var table in new[] { "roles", "acl_permissions", "role_permissions" }) {
                var ddl = Regex.Match(pristine, $@"CREATE TABLE {table} \([\s\S]*?\) ENGINE=[^;]+;").Value;
                Assert.NotEmpty(ddl);
                connection.Execute(ddl);
            }
            connection.Execute("INSERT INTO roles(id,slug,name) VALUES(1,'default','User'),(2,'custom','Custom'); " +
                "INSERT INTO acl_permissions(`key`,category,description) VALUES('command.wordquiz','custom','Preserve'); " +
                "INSERT INTO role_permissions(role_id,permission_key) VALUES(2,'command.existing')");
            var migration = File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/50_RoomWordQuizCommand.sql"));
            connection.Execute(migration);
            connection.Execute(migration);
            Assert.Equal(("custom", "Preserve"), connection.QuerySingle<(string, string)>("SELECT category,description FROM acl_permissions WHERE `key`='command.wordquiz'"));
            Assert.Equal(new[] { (1, "command.wordquiz"), (2, "command.existing") },
                connection.Query<(int, string)>("SELECT role_id,permission_key FROM role_permissions ORDER BY role_id").ToArray());
        }
        finally {
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
