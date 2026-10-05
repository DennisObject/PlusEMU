using System.Data;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class GroupConstructionDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void InitialRowsArePreparedAndCreationPublishesOnlyAfterCommit()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "group_construction_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}`");
        try
        {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE `groups` (
                    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY, name VARCHAR(50) NOT NULL, `desc` VARCHAR(255) NOT NULL,
                    badge VARCHAR(50) NOT NULL, owner_id INT UNSIGNED NOT NULL, created INT NOT NULL,
                    room_id INT UNSIGNED NOT NULL, state ENUM('0','1','2') NOT NULL DEFAULT '0', colour1 INT NOT NULL,
                    colour2 INT NOT NULL, admindeco INT NOT NULL DEFAULT 0, forum_enabled BOOL NOT NULL DEFAULT FALSE);
                CREATE TABLE group_memberships (
                    id INT UNSIGNED AUTO_INCREMENT PRIMARY KEY, group_id INT UNSIGNED NOT NULL,
                    user_id INT UNSIGNED NOT NULL, `rank` BOOL NOT NULL DEFAULT FALSE);
                CREATE TABLE group_requests (group_id INT UNSIGNED NOT NULL, user_id INT UNSIGNED NOT NULL);
                CREATE TABLE rooms (id INT UNSIGNED PRIMARY KEY, group_id INT NOT NULL DEFAULT 0);
                INSERT INTO `groups` VALUES
                    (10, 'open', 'description', 'badge', 20, 1700000000, 41, '0', 3, 4, 1, TRUE),
                    (11, 'locked', 'description', 'badge', 20, 1700000000, 41, '1', 3, 4, 1, FALSE),
                    (12, 'private', 'description', 'badge', 20, 1700000000, 41, '2', 3, 4, 1, FALSE);
                INSERT INTO group_memberships (group_id, user_id, `rank`) VALUES
                    (10, 30, FALSE), (10, 20, TRUE), (10, 10, FALSE);
                INSERT INTO group_requests VALUES (10, 40), (10, 30), (10, 25);
                INSERT INTO rooms VALUES (42, 0);
                """);
            var database = new ProbeDatabase(new MySqlConnectionStringBuilder(
                Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE")!) { Database = schema }.ConnectionString);
            var memberships = new GroupMembershipLoader(database);
            var groups = new GroupManager(TestLogging.For<GroupManager>(), database, memberships);

            Assert.True(groups.TryGetGroup(10, out var loaded));
            Assert.Equal([30, 10], loaded.GetMembers);
            Assert.Equal([20], loaded.GetAdministrators);
            Assert.Equal([25, 40], loaded.GetRequests);
            Assert.Equal(0, connection.QuerySingle<int>(
                "SELECT COUNT(*) FROM group_requests WHERE group_id = 10 AND user_id = 30"));
            Assert.True(loaded.HasForum);
            Assert.Equal(GroupType.Open, loaded.Type);
            Assert.True(groups.TryGetGroup(11, out var locked));
            Assert.Equal(GroupType.Locked, locked.Type);
            Assert.True(groups.TryGetGroup(12, out var privateGroup));
            Assert.Equal(GroupType.Private, privateGroup.Type);

            var failedId = connection.QuerySingle<int>("""
                SELECT AUTO_INCREMENT FROM information_schema.tables
                WHERE table_schema = DATABASE() AND table_name = 'groups'
                """);
            var owner = new Habbo { Id = 7, Username = "owner" };
            Assert.Throws<MySqlException>(() => groups.TryCreateGroup(
                owner, "failed", "description", 42, "badge", 3, 4, out _));
            Assert.Equal(3, connection.QuerySingle<int>("SELECT COUNT(*) FROM `groups`"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM group_memberships WHERE group_id <> 10"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT group_id FROM rooms WHERE id = 42"));
            Assert.False(groups.TryGetGroup(failedId, out _));

            connection.Execute("CREATE TABLE room_rights (room_id INT UNSIGNED NOT NULL, user_id INT NOT NULL); INSERT INTO room_rights VALUES (42, 8)");
            Assert.True(groups.TryCreateGroup(owner, "created", "description", 42, "badge", 3, 4, out var created));
            Assert.True(created.IsMember(owner.Id));
            Assert.True(created.IsAdmin(owner.Id));
            Assert.Equal([owner.Id], created.GetAdministrators);
            Assert.Empty(created.GetMembers);
            Assert.Equal(1, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM group_memberships
                WHERE group_id = @groupId AND user_id = @ownerId AND `rank` = TRUE
                """, new { groupId = created.Id, ownerId = owner.Id }));
            Assert.Equal(created.Id, connection.QuerySingle<int>("SELECT group_id FROM rooms WHERE id = 42"));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM room_rights WHERE room_id = 42"));
        }
        finally
        {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    private sealed class ProbeDatabase(string connectionString) : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection() => new MySqlConnection(connectionString);
    }
}
