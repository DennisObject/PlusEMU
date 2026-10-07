using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using MySqlConnector;
using Plus.Database;
using Plus.HabboHotel.Groups.Forums;
using Xunit;

namespace Plus.Tests
{
    public sealed class GroupForumDatabaseTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 30, 10, TimeSpan.Zero);
        private static readonly ForumViewer Owner = new(1, false);
        private static readonly ForumViewer Member = new(2, false);
        private static readonly ForumViewer Admin = new(3, false);
        private static readonly ForumViewer Outsider = new(4, false);
        private static readonly ForumViewer Staff = new(5, true);

        [GroupForumDatabaseFact]
        public void NativeEmptyAndPopulatedForumsUseLocalOrdinalsAndExactPagination()
        {
            using var fixture = new Fixture();
            var store = fixture.Store;
            var empty = Assert.IsType<GroupForumSnapshot>(store.Forum(Owner, 10, Now));
            Assert.Equal((0, 0, 0), (empty.Threads, empty.Messages, empty.Unread));
            Assert.Empty(store.Threads(Owner, 10, 0, 20, Now)!.Threads);
            Assert.Null(store.Forum(Owner, 12, Now));
            Assert.Equal(2, store.List(Owner, 0, 0, 20, Now).Total);
            var first = Assert.IsType<ForumPostResult>(store.Post(Owner, 10, 0, "First exact title", "First message body", Now));
            var other = Assert.IsType<ForumPostResult>(store.Post(Member, 11, 0, "Other exact title", "Another forum body", Now));
            var reply = Assert.IsType<ForumPostResult>(store.Post(Member, 10, first.Thread.Id, "", "Distinct reply body", Now.AddSeconds(30)));
            Assert.Equal((1, 0, 1, 0, 2, 1), (first.Message.Id, first.Message.Index, other.Message.Id, other.Message.Index, reply.Message.Id, reply.Message.Index));
            Assert.Equal(2, reply.Thread.Messages);
            var page = Assert.IsType<ForumMessagesPage>(store.Messages(Owner, 10, first.Thread.Id, 1, 1, Now.AddMinutes(1)));
            var message = Assert.Single(page.Messages);
            Assert.Equal("Distinct reply body", message.Body);
            Assert.Equal("Member", message.AuthorName);
            Assert.Equal("hd-180-1", message.AuthorFigure);
            Assert.Equal(30, message.CreatedSecondsAgo);
            Assert.Equal(2, message.AuthorPosts);
            Assert.Null(store.Messages(Owner, 11, first.Thread.Id, 0, 20, Now));
            Assert.Null(store.Post(Owner, 11, first.Thread.Id, "", "Must not cross forum", Now.AddMinutes(1)));
            Assert.Null(store.ModerateMessage(Staff, 11, first.Thread.Id, 1, 20, Now));
            Assert.Equal("First exact title", store.Thread(Owner, 10, first.Thread.Id, Now)!.Title);
            Assert.Null(store.Threads(Owner, 10, -1, 20, Now));
            Assert.Null(store.Messages(Owner, 10, first.Thread.Id, 0, 51, Now));
            Assert.Empty(store.List(Owner, 3, 0, 20, Now).Forums);
            var summary = store.Forum(Owner, 10, Now.AddMinutes(1))!;
            Assert.Equal((1, 2, 2, 2, 2, "Member", 30), (summary.Threads, summary.Messages, summary.Unread, summary.LastMessageId, summary.LastAuthorId, summary.LastAuthorName, summary.LastPostSecondsAgo));
        }

        [GroupForumDatabaseFact]
        public void CurrentMembershipAndOwnerPermissionsGovernEveryMutation()
        {
            using var fixture = new Fixture();
            var store = fixture.Store;
            Assert.Null(store.Settings(Admin, 10, new(1, 1, 2, 2), Now));
            Assert.Null(store.Settings(Owner, 10, new(2, 1, 2, 2), Now));
            Assert.Null(store.Settings(Owner, 10, new(0, 0, 0, 0), Now));
            Assert.NotNull(store.Settings(Owner, 10, new(1, 1, 2, 2), Now));
            var denied = store.Forum(Outsider, 10, Now)!;
            Assert.Equal("not_member", denied.ReadError);
            Assert.Equal("not_admin", denied.StartError);
            Assert.Null(store.Threads(Outsider, 10, 0, 20, Now));
            Assert.DoesNotContain(store.List(Outsider, 0, 0, 20, Now).Forums, forum => forum.Id == 10);
            Assert.Null(store.Post(Outsider, 10, 0, "Denied title text", "Denied message text", Now));
            Assert.Null(store.Post(Member, 10, 0, "Member title text", "Member message text", Now));
            var created = store.Post(Admin, 10, 0, "Admin title text", "Admin message text", Now)!;
            Assert.NotNull(store.Post(Member, 10, created.Thread.Id, "", "Member reply text", Now));
            Assert.Null(store.UpdateThread(Member, 10, created.Thread.Id, true, true, Now));
            Assert.NotNull(store.UpdateThread(Admin, 10, created.Thread.Id, true, true, Now));
            Assert.Null(store.Post(Member, 10, created.Thread.Id, "", "Locked reply denied", Now.AddSeconds(30)));
            Assert.NotNull(store.Post(Admin, 10, created.Thread.Id, "", "Moderator locked reply", Now.AddSeconds(30)));
            fixture.Connection.Execute("UPDATE group_memberships SET `rank`='0' WHERE group_id=10 AND user_id=3");
            Assert.Null(store.ModerateThread(Admin, 10, created.Thread.Id, 10, Now));
            fixture.Connection.Execute("DELETE FROM group_memberships WHERE group_id=10 AND user_id=2");
            Assert.Null(store.Post(Member, 10, created.Thread.Id, "", "Departed member reply", Now.AddMinutes(1)));
            Assert.NotNull(store.Settings(Owner, 10, new(3, 3, 3, 3), Now));
            Assert.Null(store.Threads(Admin, 10, 0, 20, Now));
            Assert.NotNull(store.Threads(Staff, 10, 0, 20, Now));
            Assert.NotNull(store.Settings(Staff, 10, new(0, 0, 0, 2), Now));
            Assert.True(new Plus.HabboHotel.Groups.GroupSettingsStore(fixture.Database).Update(10, Plus.HabboHotel.Groups.GroupType.Open, false, false, []));
            Assert.Null(store.Forum(Owner, 10, Now));
            Assert.Null(store.Post(Staff, 10, 0, "Disabled forum title", "Disabled forum body", Now.AddMinutes(1)));
            Assert.True(new Plus.HabboHotel.Groups.GroupSettingsStore(fixture.Database).Update(10, Plus.HabboHotel.Groups.GroupType.Open, false, true, []));
            Assert.Equal(3, store.Forum(Owner, 10, Now)!.Messages);
        }

        [GroupForumDatabaseFact]
        public void HiddenContentIsRedactedAtItsExactModerationTierWithoutDeletingOrdinals()
        {
            using var fixture = new Fixture();
            var store = fixture.Store;
            var created = store.Post(Owner, 10, 0, "Private title text", "Private body text", Now)!;
            Assert.Null(store.ModerateThread(Member, 10, created.Thread.Id, 10, Now));
            Assert.Null(store.ModerateThread(Admin, 10, created.Thread.Id, 20, Now));
            Assert.Null(store.ModerateThread(Staff, 10, created.Thread.Id, 99, Now));
            Assert.NotNull(store.ModerateMessage(Admin, 10, created.Thread.Id, 1, 10, Now));
            Assert.Equal("", store.Messages(Member, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.Equal("Private body text", store.Messages(Admin, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.NotNull(store.ModerateMessage(Staff, 10, created.Thread.Id, 1, 20, Now));
            Assert.Equal("", store.Messages(Admin, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.Null(store.ModerateMessage(Admin, 10, created.Thread.Id, 1, 1, Now));
            Assert.NotNull(store.ModerateMessage(Staff, 10, created.Thread.Id, 1, 1, Now));
            Assert.Equal("Private body text", store.Messages(Member, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.NotNull(store.ModerateThread(Staff, 10, created.Thread.Id, 20, Now));
            Assert.Equal("", store.Threads(Admin, 10, 0, 20, Now)!.Threads[0].Title);
            Assert.Equal("", store.Thread(Admin, 10, created.Thread.Id, Now)!.Title);
            Assert.Equal("", store.Messages(Admin, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.Equal("Private title text", store.Thread(Staff, 10, created.Thread.Id, Now)!.Title);
            Assert.Equal("Private body text", store.Messages(Staff, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].Body);
            Assert.Null(store.ModerateThread(Owner, 10, created.Thread.Id, 1, Now));
            Assert.Null(store.Post(Admin, 10, created.Thread.Id, "", "Hidden thread reply", Now.AddMinutes(1)));
            Assert.NotNull(store.ModerateThread(Staff, 10, created.Thread.Id, 1, Now));
            Assert.Equal(1, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_messages"));
            Assert.Equal(1, store.Forum(Member, 10, Now)!.Unread);
        }

        [GroupForumDatabaseFact]
        public void MutationAcknowledgementsCannotRevealContentWhenModeratorsLackReadPermission()
        {
            using var fixture = new Fixture();
            var store = fixture.Store;
            var created = store.Post(Owner, 10, 0, "Owner private title", "Owner private message", Now)!;
            Assert.NotNull(store.Settings(Owner, 10, new(3, 3, 3, 2), Now));
            Assert.Equal("not_owner", store.Forum(Admin, 10, Now)!.ReadError);
            var replies = new object?[] {
                store.UpdateThread(Admin, 10, created.Thread.Id, true, true, Now),
                store.ModerateThread(Admin, 10, created.Thread.Id, 10, Now),
                store.ModerateMessage(Admin, 10, created.Thread.Id, 1, 10, Now)
            };
            Assert.All(replies, reply => Assert.Null(reply));
            var current = store.Thread(Owner, 10, created.Thread.Id, Now)!;
            Assert.False(current.Pinned);
            Assert.False(current.Locked);
            Assert.Equal((byte)0, current.State);
            Assert.Equal((byte)0, store.Messages(Owner, 10, created.Thread.Id, 0, 20, Now)!.Messages[0].State);
        }

        [GroupForumDatabaseFact]
        public void ReadMarkersAdvanceOnlyTheRequestedOrdinalAndCannotHideFuturePosts()
        {
            using var fixture = new Fixture();
            var store = fixture.Store;
            var created = store.Post(Owner, 10, 0, "First forum thread", "First forum message", Now)!;
            store.Post(Member, 10, created.Thread.Id, "", "Second forum message", Now);
            store.Post(Admin, 11, 0, "Second forum title", "Other forum message", Now);
            Assert.Equal(2, store.Unread(Owner));
            store.MarkRead(Owner, [new(10, 1, false)]);
            Assert.Equal(1, store.Forum(Owner, 10, Now)!.Unread);
            Assert.Equal(1, store.Thread(Owner, 10, created.Thread.Id, Now)!.Unread);
            store.MarkRead(Owner, [new(10, 0, true)]);
            Assert.Equal(1, store.Forum(Owner, 10, Now)!.Unread);
            store.MarkRead(Owner, [new(10, int.MaxValue, true)]);
            Assert.Equal(0, store.Forum(Owner, 10, Now)!.Unread);
            Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT last_message_id FROM group_forum_read_markers WHERE group_id=10 AND user_id=1"));
            store.Post(Owner, 10, created.Thread.Id, "", "Third forum message", Now.AddSeconds(30));
            Assert.Equal(1, store.Forum(Owner, 10, Now)!.Unread);
            Assert.Equal(2, store.Unread(Owner));
            store.MarkRead(Owner, [new(10, 3, true), new(11, 1, false)]);
            Assert.Equal(0, store.Unread(Owner));
            store.MarkRead(Owner, [new(10, -1, true), new(12, 10, true)]);
            Assert.Equal(2, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_read_markers"));
        }

        [GroupForumDatabaseFact]
        public async Task ConcurrentPostsSerializeOrdinalsAndGlobalUserCooldownWithoutPartialWrites()
        {
            using var fixture = new Fixture();
            var results = await Task.WhenAll(Task.Run(() => fixture.Store.Post(Owner, 10, 0, "Concurrent title one", "Concurrent body one", Now)),
                Task.Run(() => fixture.Store.Post(Member, 10, 0, "Concurrent title two", "Concurrent body two", Now)));
            Assert.All(results, result => Assert.NotNull(result));
            Assert.Equal(new[] { 1, 2 }, results.Select(result => result!.Message.Id).Order().ToArray());
            Assert.Equal(2, fixture.Store.Forum(Owner, 10, Now)!.Messages);
            var sameUser = await Task.WhenAll(Task.Run(() => fixture.Store.Post(Admin, 10, 0, "One user title one", "One user body one", Now)),
                Task.Run(() => fixture.Store.Post(Admin, 11, 0, "One user title two", "One user body two", Now)));
            Assert.Single(sameUser, result => result != null);
            Assert.Equal(3, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_messages"));
            Assert.Null(fixture.Store.Post(Admin, 11, 0, "Before cooldown title", "Before cooldown body", Now.AddSeconds(29).AddMilliseconds(999)));
            Assert.NotNull(fixture.Store.Post(Admin, 11, 0, "Boundary cooldown title", "Boundary cooldown body", Now.AddSeconds(30)));
        }

        [GroupForumDatabaseFact]
        public void FailedContentWriteRollsBackThreadOrdinalAndCooldownAndDeletionCascades()
        {
            using var fixture = new Fixture();
            fixture.Connection.Execute("CREATE TRIGGER fail_forum_body BEFORE INSERT ON group_forum_messages FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='fixture failure'");
            Assert.Throws<MySqlException>(() => fixture.Store.Post(Owner, 10, 0, "Rollback exact title", "Rollback exact body", Now));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_threads"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forums"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_post_limits"));
            fixture.Connection.Execute("DROP TRIGGER fail_forum_body");
            var created = fixture.Store.Post(Owner, 10, 0, "Committed title text", "Committed message text", Now)!;
            Assert.Equal(1, created.Message.Id);
            fixture.Store.MarkRead(Member, [new(10, 1, false)]);
            Assert.Throws<MySqlException>(() => fixture.Connection.Execute("INSERT INTO group_forum_messages(group_id,id,thread_id,message_index,author_id,body,created_at) VALUES(11,1,@thread,1,1,'wrong group',UTC_TIMESTAMP(6))", new { thread = created.Thread.Id }));
            fixture.Connection.Execute("DELETE FROM `groups` WHERE id=10");
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forums"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_threads"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_messages"));
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_read_markers"));
        }

        [GroupForumDatabaseFact]
        public void PermissionRevocationCompletingBeforeLockedMembershipReadRejectsModeration()
        {
            using var fixture = new Fixture();
            var thread = fixture.Store.Post(Owner, 10, 0, "Permission race title", "Permission race body", Now)!.Thread;
            using var revoke = fixture.Connection.BeginTransaction();
            fixture.Connection.Execute("UPDATE group_memberships SET `rank`='0' WHERE group_id=10 AND user_id=3", transaction: revoke);
            using var ready = new ManualResetEventSlim();
            var operation = Task.Run(() =>
            {
                ready.Set();

                return fixture.Store.ModerateThread(Admin, 10, thread.Id, 10, Now);
            });
            Assert.True(ready.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(operation.Wait(TimeSpan.FromMilliseconds(100)));
            revoke.Commit();
            Assert.Null(operation.GetAwaiter().GetResult());
            Assert.Equal((byte)0, fixture.Store.Thread(Owner, 10, thread.Id, Now)!.State);
        }

        [GroupForumDatabaseFact]
        public void NativeUnsignedGroupIdsKeepTheSignedPacketBoundaryExplicit()
        {
            using var fixture = new Fixture();
            fixture.Connection.Execute("INSERT INTO `groups`(id,name,`desc`,badge,owner_id,forum_enabled) VALUES(2147483647,'Maximum','Description','badge',1,TRUE)");
            Assert.Equal(int.MaxValue, fixture.Store.Forum(Owner, int.MaxValue, Now)!.Id);
            fixture.Connection.Execute("INSERT INTO `groups`(id,name,`desc`,badge,owner_id,forum_enabled) VALUES(2147483648,'Overflow','Description','badge',1,TRUE)");
            Assert.Throws<OverflowException>(() => fixture.Store.List(Owner, 0, 0, 20, Now));
        }

        internal sealed class Fixture : IDisposable
        {
            private readonly MySqlConnection _root;
            private readonly string _schema = "group_forum_" + Guid.NewGuid().ToString("N");
            public MySqlConnection Connection { get; }
            public GroupForumStore Store { get; }
            public IDatabase Database { get; }

            public Fixture()
            {
                var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable("GROUP_FORUM_DATABASE")!)
                {
                    Pooling = false,
                    AllowZeroDateTime = true,
                    ConvertZeroDateTime = true
                };
                _root = new(builder.ConnectionString);
                _root.Open();
                _root.Execute($"CREATE DATABASE `{_schema}`");
                builder.Database = _schema;
                Connection = new(builder.ConnectionString);

                try {
                    Connection.Open();
                    var dump = File.ReadAllText(Path.Combine(RepositoryRoot(), "Resources", "SQLs", "Original Database.sql"));

                    foreach (var table in new[] { "users", "groups", "group_memberships" }) {
                        var ddl = Regex.Match(dump, @"CREATE TABLE(?: IF NOT EXISTS)? `" + table + @"`\s*\([\s\S]*?\) ENGINE=[^;]*;").Value;
                        Assert.NotEmpty(ddl);
                        Connection.Execute(ddl);
                    }

                    var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "Database", "Migrations", "54_GroupForums.sql"));
                    Connection.Execute(migration);
                    Connection.Execute(migration);
                    Assert.EndsWith(migration.TrimEnd(), dump.TrimEnd());
                    Connection.Execute("""
                        INSERT INTO users(id,username,auth_ticket,look) VALUES
                          (1,'Owner','ticket1','hd-180-1'),(2,'Member','ticket2','hd-180-1'),(3,'Admin','ticket3','hd-180-1'),
                          (4,'Outsider','ticket4','hd-180-1'),(5,'Staff','ticket5','hd-180-1');
                        INSERT INTO `groups`(id,name,`desc`,badge,owner_id,forum_enabled) VALUES
                          (10,'Forum one','Description one','badge1',1,TRUE),(11,'Forum two','Description two','badge2',1,TRUE),
                          (12,'Disabled forum','Description three','badge3',1,FALSE);
                        INSERT INTO group_memberships(group_id,user_id,`rank`) VALUES(10,2,'0'),(10,3,'1'),(11,3,'2');
                        """);
                    Database = new ProbeDatabase(builder.ConnectionString);
                    Store = new(Database);
                }
                catch {
                    Connection.Dispose();
                    _root.Execute($"DROP DATABASE `{_schema}`");
                    _root.Dispose();
                    throw;
                }
            }

            public void Dispose()
            {
                Connection.Dispose();

                try {
                    _root.Execute($"DROP DATABASE `{_schema}`");
                }
                finally {
                    _root.Dispose();
                }
            }
        }

        private sealed class ProbeDatabase(string connectionString) : IDatabase
        {
            public bool IsConnected() => true;
            public IDbConnection Connection() => new MySqlConnection(connectionString);
        }

        internal static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Plus Emulator.csproj"))) {
                directory = directory.Parent;
            }

            return directory!.FullName;
        }
    }

    public sealed class GroupForumDatabaseFactAttribute : FactAttribute
    {
        public GroupForumDatabaseFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROUP_FORUM_DATABASE"))) {
                Skip = "Set GROUP_FORUM_DATABASE for isolated native-schema group forum tests.";
            }
        }
    }
}
