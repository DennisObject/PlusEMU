using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Groups;
using Xunit;

namespace Plus.Tests;

public class GroupParticipationDatabaseTests
{
    [GroupParticipationDatabaseFact]
    public async Task OpenJoinCommitsOnceAndNeverDuplicatesAMembership()
    {
        await WithSchema(async connectionString =>
        {
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Equal(GroupJoinOutcome.Inserted, store.Join(7, 1, false, 1500));
            Assert.Equal(GroupJoinOutcome.AlreadyPresent, store.Join(7, 1, false, 1500));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id = 7 AND group_id = 1"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task LockedJoinCommitsOneRequestAndKeepsItPending()
    {
        await WithSchema(async connectionString =>
        {
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Equal(GroupJoinOutcome.Inserted, store.Join(7, 2, true, 1500));
            Assert.Equal(GroupJoinOutcome.AlreadyPresent, store.Join(7, 2, true, 1500));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests WHERE user_id = 7 AND group_id = 2"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id = 7 AND group_id = 2"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task MembershipLimitBoundaryRefusesTheNextGroupWithoutWriting()
    {
        await WithSchema(async connectionString =>
        {
            using (var setup = new MySqlConnection(connectionString))
            {
                setup.Open();
                setup.Execute("INSERT INTO group_memberships (user_id, group_id) VALUES (7, 1), (7, 2)");
            }
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Equal(GroupJoinOutcome.LimitReached, store.Join(7, 3, false, 2));
            Assert.Equal(GroupJoinOutcome.Inserted, store.Join(7, 3, false, 3));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id = 7"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task ConcurrentJoinsAcrossGroupsCannotExceedTheAccountLimit()
    {
        await WithSchema(async connectionString =>
        {
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            var outcomes = await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() => store.Join(7, index % 2 == 0 ? 1 : 2, false, 1))));

            Assert.Equal(1, outcomes.Count(outcome => outcome == GroupJoinOutcome.Inserted));
            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships WHERE user_id = 7"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task DeletedGroupRefusesJoinAndFavouriteWithoutWriting()
    {
        await WithSchema(async connectionString =>
        {
            using (var setup = new MySqlConnection(connectionString))
            {
                setup.Open();
                setup.Execute("DELETE FROM `groups` WHERE id = 2");
            }
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Equal(GroupJoinOutcome.Refused, store.Join(7, 2, true, 1500));
            Assert.False(store.SaveFavourite(7, 2));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests"));
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT groupid FROM user_statistics WHERE id = 7"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task MissingAccountIsRefusedAndWritesNothing()
    {
        await WithSchema(async connectionString =>
        {
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.Equal(GroupJoinOutcome.Refused, store.Join(99, 1, false, 1500));
            Assert.False(store.SaveFavourite(99, 1));

            using var connection = new MySqlConnection(connectionString);
            connection.Open();
            Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_memberships"));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task FailedRequestWriteRollsBackAndLeavesTheAccountUsable()
    {
        await WithSchema(async connectionString =>
        {
            using (var setup = new MySqlConnection(connectionString))
            {
                setup.Open();
                setup.Execute("RENAME TABLE group_requests TO group_requests_parked");
            }
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.ThrowsAny<MySqlException>(() => store.Join(7, 2, true, 1500));

            using (var restore = new MySqlConnection(connectionString))
            {
                restore.Open();
                restore.Execute("RENAME TABLE group_requests_parked TO group_requests");
                Assert.Equal(0, restore.ExecuteScalar<int>("SELECT COUNT(*) FROM group_requests"));
            }
            Assert.Equal(GroupJoinOutcome.Inserted, store.Join(7, 2, true, 1500));
        });
    }

    [GroupParticipationDatabaseFact]
    public async Task FavouriteWritesCommitIncludingZeroAndRefuseMissingStats()
    {
        await WithSchema(async connectionString =>
        {
            var store = new GroupParticipationStore(new HabbiconDatabaseTests.TestDatabase(connectionString));

            Assert.True(store.SaveFavourite(7, 3));
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                Assert.Equal(3, connection.ExecuteScalar<int>("SELECT groupid FROM user_statistics WHERE id = 7"));
            }
            Assert.True(store.SaveFavourite(7, 0));
            using (var connection = new MySqlConnection(connectionString))
            {
                connection.Open();
                Assert.Equal(0, connection.ExecuteScalar<int>("SELECT groupid FROM user_statistics WHERE id = 7"));
            }
            Assert.False(store.SaveFavourite(8, 3));
        });
    }

    private static async Task WithSchema(Func<string, Task> body)
    {
        var server = Environment.GetEnvironmentVariable("PLUS_GROUP_PARTICIPATION_TEST_CONNECTION_STRING")!;
        var schema = "task_group_participation_tests_" + Guid.NewGuid().ToString("N")[..12];
        var options = new MySqlConnectionStringBuilder(server) { Database = schema, AllowZeroDateTime = true, ConvertZeroDateTime = true };
        using (var admin = new MySqlConnection(server))
        {
            admin.Open();
            admin.Execute($"CREATE DATABASE `{schema}`");
        }
        try
        {
            using var connection = new MySqlConnection(options.ConnectionString);
            connection.Open();
            var dump = File.ReadAllText(HabbiconPacketTests.Repo("Resources/SQLs/Original Database.sql"));
            foreach (var table in new[] { "users", "groups", "group_memberships", "group_requests", "user_stats" })
                connection.Execute(ExtractTable(dump, table));
            connection.Execute("ALTER TABLE user_stats RENAME TO user_statistics");
            connection.Execute("INSERT INTO users (id, username, auth_ticket) VALUES (7, 'member', ''), (8, 'no_stats', '')");
            connection.Execute("INSERT INTO user_statistics (id) VALUES (7)");
            connection.Execute("INSERT INTO `groups` (id, name, `desc`, badge, owner_id) VALUES (1, 'One', '', 'b', 1), (2, 'Two', '', 'b', 1), (3, 'Three', '', 'b', 1)");
            await body(options.ConnectionString);
        }
        finally
        {
            using var admin = new MySqlConnection(server);
            admin.Open();
            admin.Execute($"DROP DATABASE IF EXISTS `{schema}`");
        }
    }

    // Each definition comes from the pristine dump, so the probe cannot drift from the shipped tables.
    private static string ExtractTable(string dump, string table)
    {
        int start = dump.IndexOf($"CREATE TABLE `{table}`", StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException($"Pristine dump has no table {table}.");
        int end = dump.IndexOf(';', dump.IndexOf("ENGINE=", start, StringComparison.Ordinal));
        return dump[start..(end + 1)];
    }
}

public sealed class GroupParticipationDatabaseFactAttribute : Xunit.FactAttribute
{
    public GroupParticipationDatabaseFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUS_GROUP_PARTICIPATION_TEST_CONNECTION_STRING")))
            Skip = "Set PLUS_GROUP_PARTICIPATION_TEST_CONNECTION_STRING to a server that can create and drop disposable task_group_participation_tests_ schemas.";
    }
}
