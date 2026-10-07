using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Groups.Forums;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Groups.Forums;
using Plus.HabboHotel.Groups.Forums;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests
{
    public sealed class GroupForumPacketTests
    {
        private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        private static readonly GroupForumSnapshot Forum = new(10, "Forum", "Description", "badge", 3, 9, 5, 9, 7, "Last", 60,
            new(1, 1, 2, 2), "read", "post", "start", "moderate", true, false);
        private static readonly ForumThreadSnapshot Thread = new(20, 7, "Author", "Thread", true, false, 10, 3, 2, 9, 8, "Last", 5, 10, 9, "Mod", 2);
        private static readonly ForumMessageSnapshot Message = new(9, 2, 7, "Author", "figure", 10, "Content", 20, 9, "Mod", 2, 4);
        private static readonly object[] ForumFields = [10, "Forum", "Description", "badge", 3, 0, 9, 5, 9, 7, "Last", 60];
        private static readonly object[] ThreadFields = [20, 7, "Author", "Thread", true, false, 10, 3, 2, 9, 8, "Last", 5, (byte)10, 9, "Mod", 2];
        private static readonly object[] MessageFields = [9, 2, 7, "Author", "figure", 10, "Content", (byte)20, 9, "Mod", 2, 4];

        [Fact]
        public void EveryForumResponseMatchesTheOfficialParserFieldOrder()
        {
            Check(new ForumDataComposer(Forum), [.. ForumFields, 1, 1, 2, 2, "read", "post", "start", "moderate", "", true, false]);
            Check(new ForumsListDataComposer(new(2, 8, 3, [Forum])), [2, 8, 3, 1, .. ForumFields]);
            Check(new ThreadsListDataComposer(new(10, 2, [Thread])), [10, 2, 1, .. ThreadFields]);
            Check(new ThreadDataComposer(new(10, 20, 3, [Message])), [10, 20, 3, 1, .. MessageFields]);
            Check(new ThreadCreatedComposer(10, Thread), [10, .. ThreadFields]);
            Check(new ThreadUpdatedComposer(10, Thread), [10, .. ThreadFields]);
            Check(new ThreadReplyComposer(10, 20, Message), [10, 20, .. MessageFields]);
            Check(new PostUpdatedComposer(10, 20, Message), [10, 20, .. MessageFields]);
            Check(new ForumsUnreadCountComposer(8), [8]);
        }

        [Fact]
        public async Task AllRequestHandlersDecodeDistinctFieldsInOfficialOrder()
        {
            var calls = new List<(string Name, object?[] Args)>();
            var service = CatalogSnapshotTestSupport.Proxy<IGroupForumService>((name, args) => { calls.Add((name, args)); return null; });
            var (client, _) = Session(new Habbo { Id = 7, Access = UserAccess.Empty });
            var cases = new (IPacketEvent Handler, object[] Values, string Method)[] {
                (new GetForumStatsEvent(service), [10], "ShowForum"),
                (new GetForumsListDataEvent(service), [2, 3, 4], "ShowForums"),
                (new GetThreadsListDataEvent(service), [10, 3, 4], "ShowThreads"),
                (new GetThreadDataEvent(service), [10, 20, 3, 4], "ShowMessages"),
                (new GetForumThreadEvent(service), [10, 20], "ShowThread"),
                (new PostGroupContentEvent(service), [10, 20, "title text", "body text"], "Post"),
                (new DeleteGroupThreadEvent(service), [10, 20, 10], "ModerateThread"),
                (new DeleteGroupPostEvent(service), [10, 20, 30, 20], "ModerateMessage"),
                (new UpdateThreadEvent(service), [10, 20, true, false], "UpdateThread"),
                (new GetForumsUnreadCountEvent(service), [], "ShowUnread")
            };
            foreach (var (handler, values, method) in cases) {
                var packet = Incoming(values);
                await handler.Parse(client, packet);
                var call = calls[^1];
                Assert.Equal(method, call.Name);
                Assert.Same(client, call.Args[0]);
                Assert.Equal(values, call.Args[1..]);
                Assert.False(packet.HasDataRemaining());
            }
            var settings = Incoming(10, 1, 2, 3, 2);
            await new UpdateForumSettingsEvent(service).Parse(client, settings);
            Assert.Equal("UpdateSettings", calls[^1].Name);
            Assert.Equal(new ForumPermissions(1, 2, 3, 2), calls[^1].Args[2]);
            Assert.False(settings.HasDataRemaining());
            var markers = Incoming(2, 10, 31, false, 11, 32, true);
            await new UpdateForumReadMarkersEvent(service).Parse(client, markers);
            Assert.Equal("MarkRead", calls[^1].Name);
            Assert.Equal(new[] { new ForumReadMarker(10, 31, false), new ForumReadMarker(11, 32, true) }, Assert.IsType<ForumReadMarker[]>(calls[^1].Args[1]));
            Assert.False(markers.HasDataRemaining());
            var count = calls.Count;
            await new UpdateForumReadMarkersEvent(service).Parse(client, Incoming(101));
            await new UpdateForumReadMarkersEvent(service).Parse(client, Incoming(-1));
            Assert.Equal(count, calls.Count);
        }

        [Fact]
        public void ServiceUsesCapturedClockFiltersBeforeWriteAndPublishesOnlyItsCommittedSnapshot()
        {
            var (client, sent) = Session(new Habbo { Id = 7, Access = UserAccess.Empty });
            var filterCalls = new List<string>();
            var filter = CatalogSnapshotTestSupport.Proxy<IWordFilterManager>((name, args) => {
                Assert.Equal("CheckMessage", name);
                filterCalls.Add((string)args[0]!);
                return ((string)args[0]!).Replace("bad", "***");
            });
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((name, args) => {
                Assert.Equal("Post", name);
                Assert.Equal(new ForumViewer(7, false), args[0]);
                Assert.Equal(Now, args[5]);
                Assert.Equal("A *** title text", args[3]);
                Assert.Equal("A *** message body", args[4]);
                Assert.Empty(sent);
                return new ForumPostResult(Thread, Message, true);
            });
            var service = new GroupForumService(store, new FixedClock(), filter, NullLogger<GroupForumService>.Instance);
            service.Post(client, 10, 0, "  A bad title text ", " A bad message body  ");
            Assert.Equal(new[] { "A bad title text", "A bad message body" }, filterCalls);
            Assert.Equal(ServerPacketHeader.ThreadCreatedComposer, Assert.Single(sent).Header);
            var expected = new RecordingPacket();
            new ThreadCreatedComposer(10, Thread).Compose(expected);
            var decode = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = sent[0].Payload };
            Assert.Equal(10, decode.ReadInt());
            Assert.Equal(20, decode.ReadInt());
        }

        [Fact]
        public void StaffPrivilegeIsTheExistingAclAndClosedOrMalformedPostsNeverReachTheStore()
        {
            var user = new Habbo { Id = 7, Access = UserAccess.Create([], [new(PermissionKeys.ModerationTool, false), new(PermissionKeys.ChatFilterBypass, false)]) };
            var (client, sent) = Session(user);
            var calls = 0;
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((name, args) => {
                calls++;
                Assert.Equal(new ForumViewer(7, true), args[0]);
                return Forum;
            });
            var filter = CatalogSnapshotTestSupport.Proxy<IWordFilterManager>((_, _) => throw new InvalidOperationException("unused filter"));
            var service = new GroupForumService(store, new FixedClock(), filter, NullLogger<GroupForumService>.Instance);
            service.ShowForum(client, 10);
            Assert.Equal(1, calls);
            service.Post(client, 10, 0, "short", "Valid message body");
            service.Post(client, 10, 0, "Valid title text", new string('x', 4001));
            Assert.Equal(1, calls);
            user.Persistence = CatalogSnapshotTestSupport.Proxy<IUserPersistenceService>((_, _) => null);
            user.Save();
            sent.Clear();
            service.ShowForum(client, 10);
            service.Post(client, 10, 0, "Valid title text", "Valid message body");
            Assert.Equal(1, calls);
            Assert.Empty(sent);
        }

        [Fact]
        public void DisposedSessionCannotReadPostOrModerateEvenWhileItRetainsItsClient()
        {
            var user = new Habbo { Id = 7, Access = UserAccess.Empty };
            var (client, sent) = Session(user);
            user.Dispose();
            Assert.Same(client, user.Client);
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((_, _) => throw new InvalidOperationException("closed session must not reach store"));
            var filter = CatalogSnapshotTestSupport.Proxy<IWordFilterManager>((_, _) => throw new InvalidOperationException("closed session must not filter"));
            var service = new GroupForumService(store, new FixedClock(), filter, NullLogger<GroupForumService>.Instance);
            service.ShowForum(client, 10);
            service.ShowForums(client, 0, 0, 20);
            service.ShowThreads(client, 10, 0, 20);
            service.ShowThread(client, 10, 20);
            service.ShowMessages(client, 10, 20, 0, 20);
            service.Post(client, 10, 0, "Valid title text", "Valid message body");
            service.UpdateSettings(client, 10, new(0, 0, 0, 2));
            service.UpdateThread(client, 10, 20, true, true);
            service.ModerateThread(client, 10, 20, 10);
            service.ModerateMessage(client, 10, 20, 30, 10);
            service.MarkRead(client, [new(10, 30, false)]);
            service.ShowUnread(client);
            Assert.Empty(sent);
        }

        [Fact]
        public void DisposingTheSessionDuringTheStoreOperationSuppressesItsLateResponse()
        {
            var user = new Habbo { Id = 7, Access = UserAccess.Empty };
            var (client, sent) = Session(user);
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((name, _) => {
                Assert.Equal("Forum", name);
                user.Dispose();
                return Forum;
            });
            var service = new GroupForumService(store, new FixedClock(), null!, NullLogger<GroupForumService>.Instance);
            service.ShowForum(client, 10);
            Assert.True(user.AccessClosed);
            Assert.Same(client, user.Client);
            Assert.Empty(sent);
        }

        [Fact]
        public void ForeignSessionRetainingTheSameHabboCannotIssueForumRequests()
        {
            var user = new Habbo { Id = 7, Access = UserAccess.Empty };
            var (foreign, sent) = Session(user);
            var (current, _) = Session(user);
            Assert.Same(current, user.Client);
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((_, _) => throw new InvalidOperationException("foreign session must not reach store"));
            var service = new GroupForumService(store, new FixedClock(), null!, NullLogger<GroupForumService>.Instance);
            service.ShowForum(foreign, 10);
            service.Post(foreign, 10, 0, "Valid title text", "Valid message body");
            service.ModerateMessage(foreign, 10, 20, 30, 10);
            Assert.Empty(sent);
        }

        [Fact]
        public void ReplacingTheClientDuringAStoreReadSuppressesTheOldSessionResponse()
        {
            var user = new Habbo { Id = 7, Access = UserAccess.Empty };
            var (old, sent) = Session(user);
            var (replacement, _) = HabbiconTestSupport.Client(user);
            Assert.Same(old, user.Client);
            var store = CatalogSnapshotTestSupport.Proxy<IGroupForumStore>((_, _) => {
                user.Client = replacement;
                return Forum;
            });
            var service = new GroupForumService(store, new FixedClock(), null!, NullLogger<GroupForumService>.Instance);
            service.ShowForum(old, 10);
            Assert.False(user.AccessClosed);
            Assert.Same(replacement, user.Client);
            Assert.Empty(sent);
        }

        [GroupForumDatabaseFact]
        public async Task PublicPacketPathPostsAndReadsTheCommittedNativeForumAndHidesFailedWrites()
        {
            using var fixture = new GroupForumDatabaseTests.Fixture();
            var (client, sent) = Session(new Habbo { Id = 1, Access = UserAccess.Empty });
            var filter = CatalogSnapshotTestSupport.Proxy<IWordFilterManager>((_, args) => args[0]);
            var service = new GroupForumService(fixture.Store, new FixedClock(), filter, NullLogger<GroupForumService>.Instance);
            await new GetForumStatsEvent(service).Parse(client, Incoming(10));
            Assert.Equal(ServerPacketHeader.ForumDataComposer, Assert.Single(sent).Header);
            sent.Clear();
            await new PostGroupContentEvent(service).Parse(client, Incoming(10, 0, "Public exact title", "Public distinct body"));
            Assert.Equal(ServerPacketHeader.ThreadCreatedComposer, Assert.Single(sent).Header);
            var thread = Assert.Single(fixture.Store.Threads(new(1, false), 10, 0, 20, Now)!.Threads);
            Assert.Equal("Public exact title", thread.Title);
            Assert.Equal("Public distinct body", fixture.Store.Messages(new(1, false), 10, thread.Id, 0, 20, Now)!.Messages[0].Body);
            sent.Clear();
            await new GetThreadDataEvent(service).Parse(client, Incoming(10, thread.Id, 0, 20));
            Assert.Equal(ServerPacketHeader.ThreadDataComposer, Assert.Single(sent).Header);
            sent.Clear();
            await new PostGroupContentEvent(service).Parse(client, Incoming(10, thread.Id, "", "Cooldown denied body"));
            Assert.Empty(sent);
            Assert.Equal(1, fixture.Store.Forum(new(1, false), 10, Now)!.Messages);
            sent.Clear();
            await new UpdateForumReadMarkersEvent(service).Parse(client, Incoming(1, 10, 1, false));
            Assert.Equal(ServerPacketHeader.ForumsUnreadCountComposer, Assert.Single(sent).Header);
            Assert.Equal(0, fixture.Store.Unread(new(1, false)));
        }

        [GroupForumDatabaseFact]
        public async Task FailedNativePostPublishesNothingAndLeavesNoThreadOrCooldown()
        {
            using var fixture = new GroupForumDatabaseTests.Fixture();
            fixture.Connection.Execute("CREATE TRIGGER reject_forum_post BEFORE INSERT ON group_forum_messages FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='fixture failure'");
            var (client, sent) = Session(new Habbo { Id = 1, Access = UserAccess.Empty });
            var filter = CatalogSnapshotTestSupport.Proxy<IWordFilterManager>((_, args) => args[0]);
            var service = new GroupForumService(fixture.Store, new FixedClock(), filter, NullLogger<GroupForumService>.Instance);
            await new PostGroupContentEvent(service).Parse(client, Incoming(10, 0, "Rejected exact title", "Rejected exact body"));
            Assert.Empty(sent);
            Assert.Empty(fixture.Store.Threads(new(1, false), 10, 0, 20, Now)!.Threads);
            Assert.Equal(0, fixture.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM group_forum_post_limits"));
        }

        [Fact]
        public void EveryForumSymbolResolvesToItsCurrentHeaderWithoutCollisions()
        {
            var incoming = new Dictionary<string, uint> {
                [nameof(GetForumStatsEvent)] = 3149, [nameof(GetForumsListDataEvent)] = 873,
                [nameof(GetThreadsListDataEvent)] = 436, [nameof(GetThreadDataEvent)] = 232,
                [nameof(GetForumThreadEvent)] = 3900, [nameof(PostGroupContentEvent)] = 3529,
                [nameof(DeleteGroupThreadEvent)] = 1397, [nameof(DeleteGroupPostEvent)] = 286,
                [nameof(UpdateThreadEvent)] = 3045, [nameof(UpdateForumSettingsEvent)] = 2214,
                [nameof(UpdateForumReadMarkersEvent)] = 1855, [nameof(GetForumsUnreadCountEvent)] = 2908
            };
            var outgoing = new Dictionary<string, uint> {
                [nameof(ForumDataComposer)] = 3011, [nameof(ForumsListDataComposer)] = 3001,
                [nameof(ThreadsListDataComposer)] = 1073, [nameof(ThreadDataComposer)] = 509,
                [nameof(ThreadCreatedComposer)] = 1862, [nameof(ThreadUpdatedComposer)] = 2528,
                [nameof(ThreadReplyComposer)] = 2049, [nameof(PostUpdatedComposer)] = 324,
                [nameof(ForumsUnreadCountComposer)] = 2379
            };
            foreach (var (type, headers) in new[] { (typeof(ClientPacketHeader), incoming), (typeof(ServerPacketHeader), outgoing) }) {
                var ids = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (uint)field.GetRawConstantValue()!).Where(id => id > 0).ToArray();
                Assert.Equal(ids.Length, ids.Distinct().Count());
                Assert.All(headers.Keys, name => Assert.NotNull(type.GetField(name)));
            }
            foreach (var file in Directory.GetFiles(Path.Combine(GroupForumDatabaseTests.RepositoryRoot(), "Resources", "Revisions"), "*.json")) {
                using var json = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var (key, headers, type) in new[] { ("IncomingHeaders", incoming, typeof(ClientPacketHeader)), ("OutgoingHeaders", outgoing, typeof(ServerPacketHeader)) }) {
                    foreach (var (name, header) in headers) {
                        var expected = Path.GetFileName(file) == "example.json" && name is not nameof(GetForumThreadEvent) and not nameof(UpdateForumReadMarkersEvent) and not nameof(GetForumsUnreadCountEvent) and not nameof(ForumsUnreadCountComposer)
                            ? (uint)type.GetField(name)!.GetRawConstantValue()! : header;
                        Assert.Equal(expected, json.RootElement.GetProperty(key).GetProperty(name).GetUInt32());
                    }
                }
            }
        }

        private static (Plus.Communication.Flash.FlashGameClient Client, List<(uint Header, byte[] Payload)> Sent) Session(Habbo user)
        {
            var session = HabbiconTestSupport.Client(user);
            user.Client = session.Client;
            return session;
        }

        private static void Check(IServerPacket composer, object[] expected)
        {
            var packet = new RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(expected, packet.Writes);
        }

        private sealed class FixedClock : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => Now;
        }
    }
}
