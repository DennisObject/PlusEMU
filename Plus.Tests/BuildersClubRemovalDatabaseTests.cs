using Dapper;
using MySqlConnector;
using Xunit;

namespace Plus.Tests;

public sealed class BuildersClubRemovalDatabaseTests
{
    [RoomComponentDatabaseFact]
    public void MigrationRemovesOnlyBuildersClubConfiguration()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "builders_club_removal_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4 COLLATE utf8mb4_general_ci");

        try {
            connection.Execute($"USE `{schema}`");
            connection.Execute("""
                CREATE TABLE catalog_pages (
                    id INT PRIMARY KEY, parent_id INT NOT NULL, caption VARCHAR(35) NOT NULL,
                    catalog_mode ENUM('NORMAL','BUILDERS_CLUB') NOT NULL DEFAULT 'NORMAL');
                CREATE TABLE catalog_items (id INT PRIMARY KEY, page_id INT NOT NULL, item_id VARCHAR(120) NOT NULL);
                CREATE TABLE furniture (id INT PRIMARY KEY, item_name VARCHAR(70) NOT NULL);
                CREATE TABLE items (id INT PRIMARY KEY, user_id INT NOT NULL, base_item INT NOT NULL);
                CREATE TABLE reward_track_tasks (
                    track_id VARCHAR(64) NOT NULL, id VARCHAR(64) NOT NULL, action_type VARCHAR(64) NOT NULL,
                    PRIMARY KEY (track_id, id)) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
                CREATE TABLE reward_track_task_levels (
                    track_id VARCHAR(64) NOT NULL, task_id VARCHAR(64) NOT NULL, level INT NOT NULL,
                    PRIMARY KEY (track_id, task_id, level));
                CREATE TABLE users_reward_track_tasks (
                    user_id INT NOT NULL, track_id VARCHAR(64) NOT NULL, task_id VARCHAR(64) NOT NULL,
                    PRIMARY KEY (user_id, track_id, task_id)) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
                CREATE TABLE catalog_admin_log (
                    id INT PRIMARY KEY, catalog_type ENUM('NORMAL','BUILDER') NOT NULL);

                INSERT INTO catalog_pages VALUES
                    (1, -1, 'normal', 'NORMAL'),
                    (2, 1, 'normal child', 'NORMAL'),
                    (9027, -1, 'ordinary repurposed page', 'NORMAL'),
                    (9200, -1, 'mode builders club', 'BUILDERS_CLUB'),
                    (9201, 9200, 'tagged mode child', 'BUILDERS_CLUB'),
                    (9202, 9201, 'untagged descendant', 'NORMAL');
                INSERT INTO catalog_items VALUES
                    (1, 2, 'normal'), (2, 9027, 'ordinary-9027'), (3, 9201, 'tagged-bc'), (4, 9202, 'descendant-bc');
                INSERT INTO furniture VALUES (65000, 'bc_block_0');
                INSERT INTO items VALUES (10, 7, 65000);
                INSERT INTO reward_track_tasks VALUES
                    ('track', 'normal', 'room_chat'),
                    ('track', 'place_builders_club_furni', 'place_builders_club_furni');
                INSERT INTO reward_track_task_levels VALUES
                    ('track', 'normal', 1), ('track', 'place_builders_club_furni', 1);
                INSERT INTO users_reward_track_tasks VALUES
                    (7, 'track', 'normal'), (7, 'track', 'place_builders_club_furni');
                INSERT INTO catalog_admin_log VALUES (1, 'NORMAL'), (2, 'BUILDER');
                """);

            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Database/Migrations/32_RemoveBuildersClub.sql")));

            Assert.Equal([1, 2, 9027], connection.Query<int>("SELECT id FROM catalog_pages ORDER BY id"));
            Assert.Equal([1, 2], connection.Query<int>("SELECT id FROM catalog_items ORDER BY id"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture WHERE id = 65000"));
            Assert.Equal(1, connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE base_item = 65000"));
            Assert.Equal(["normal"], connection.Query<string>("SELECT id FROM reward_track_tasks"));
            Assert.Equal(["normal"], connection.Query<string>("SELECT task_id FROM reward_track_task_levels"));
            Assert.Equal(["normal"], connection.Query<string>("SELECT task_id FROM users_reward_track_tasks"));
            Assert.Equal([1], connection.Query<int>("SELECT id FROM catalog_admin_log"));
            Assert.Equal(0, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE() AND table_name = 'catalog_pages' AND column_name = 'catalog_mode'
                """));
            Assert.Equal("enum('NORMAL')", connection.QuerySingle<string>("""
                SELECT COLUMN_TYPE FROM information_schema.columns
                WHERE table_schema = DATABASE() AND table_name = 'catalog_admin_log' AND column_name = 'catalog_type'
                """));
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }

    [RoomComponentDatabaseFact]
    public void PristineSchemaContainsNoBuildersClubConfiguration()
    {
        using var connection = new MySqlConnection(Environment.GetEnvironmentVariable("ROOM_COMPONENT_DATABASE"));
        connection.Open();
        var schema = "builders_club_pristine_" + Guid.NewGuid().ToString("N");
        connection.Execute($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");

        try {
            connection.Execute($"USE `{schema}`");
            connection.Execute(File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql")), commandTimeout: 900);

            Assert.Equal(0, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = DATABASE() AND table_name = 'catalog_pages' AND column_name = 'catalog_mode'
                """));
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM catalog_pages WHERE id = 9027 OR parent_id = 9027"));
            Assert.Equal(0, connection.QuerySingle<int>("""
                SELECT COUNT(*) FROM reward_track_tasks
                WHERE action_type = 'place_builders_club_furni' OR id = 'place_builders_club_furni'
                """));
            Assert.True(connection.QuerySingle<int>("SELECT COUNT(*) FROM furniture WHERE LEFT(item_name, 3) = 'bc_'") > 0);
            // The dump's item 1 belongs to user 1, which the dump does not have, so migration 67 removes it.
            Assert.Equal(0, connection.QuerySingle<int>("SELECT COUNT(*) FROM items WHERE id = 1"));
        }
        finally {
            connection.Execute("USE information_schema");
            connection.Execute($"DROP DATABASE `{schema}`");
        }
    }
}
