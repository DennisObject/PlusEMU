using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Outgoing;
using Microsoft.IO;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class ModeratorTicketSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2042, 2, 3, 12, 0, 4, TimeSpan.Zero);
    private static ModerationTicket Ticket() => new(10, 5, 6, Now.AddSeconds(-4), 4,
        new Habbo { Id = 1, Username = "Sender" }, new Habbo { Id = 2, Username = "Target" }, "help", null, ["chat"])
    { Moderator = new Habbo { Id = 3, Username = "Staff" } };

    [Fact]
    public void SupportAndInitializationKeepTheirDistinctFieldOrderingAndFreezeModels()
    {
        var ticket = Ticket();
        var captured = ModeratorTicketService.Capture(ticket, 3, Now);
        var support = new ModeratorSupportTicketComposer(captured);
        var init = new ModeratorInitComposer(new(["warn"], ["room"], [captured]));
        ticket.Sender.Username = "changed";
        ticket.Moderator = null;
        ticket.Issue = "changed";
        ticket.Answered = true;
        var expectedSupport = new object[] { 10, 2, 5, 6, 4000, 4, 0, 1, "Sender", 2, "Target", 3, "Staff", "help", (uint)0, 0 };
        var expectedInit = new object[] { 1, 10, 2, 5, 6, 4000, 4, 1, 1, "Sender", 2, "Target", 3, "Staff", "help", (uint)0, 0,
            1, "warn", 0, true, true, true, true, true, true, true, 1, "room" };
        Assert.Equal(expectedSupport, Compose(support));
        Assert.Equal(expectedSupport, Compose(support));
        Assert.Equal(expectedInit, Compose(init));
        Assert.Equal(expectedInit, Compose(init));
    }

    [Fact]
    public void ChatlogAndPendingCallsPreserveTheirExactFields()
    {
        var ticket = Ticket();
        var time = ticket.CreatedAt.UtcDateTime.ToShortTimeString();
        var chats = ticket.ReportedChats.ToImmutableArray();
        var composer = new ModeratorTicketChatlogComposer(new(10, 1, 2, 42, "HQ", ticket.CreatedAt, "Target", chats));
        ticket.ReportedChats[0] = "changed";
        Assert.Equal(new object[] { 10, 1, 2, (uint)42, (byte)1, (short)2, "roomName", (byte)2, "HQ", "roomId", (byte)1,
            (uint)42, (short)1, time, 10, "Target", "chat", false }, Compose(composer));
        Assert.Equal(new object[] { 1, "10", time, "help" }, Compose(new CallForHelpPendingCallsComposer(ModeratorTicketService.Capture(ticket, 3, Now))));
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(3000000, int.MaxValue)]
    public void TicketAgeIsBoundedWithoutOverflow(int seconds, int expected)
    {
        var ticket = Ticket();
        ticket.CreatedAt = Now.AddSeconds(-seconds);
        Assert.Equal(expected, ModeratorTicketService.Capture(ticket, 3, Now).AgeMilliseconds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChatlogsUseCanonicalRoomDataAndRefuseMissingRooms(bool exists)
    {
        var ticket = Ticket();
        ticket.Room = new RoomData { Id = 42, Name = "stale" };
        var manager = Proxy<IModerationManager>((method, args) =>
        {
            Assert.Equal("TryGetTicket", method);
            args[1] = ticket;

            return true;
        });
        var rooms = Proxy<IRoomDataLoader>((method, args) =>
        {
            Assert.Equal("TryGetData", method);
            Assert.Equal(42u, args[0]);
            args[1] = exists ? new RoomData { Id = 42, Name = "current" } : null;

            return exists;
        });
        var (actor, sent) = HabbiconTestSupport.Client(ticket.Moderator!);
        var service = new ModeratorTicketService(manager, null!, null!, new FailingStore(), TimeProvider.System, rooms);

        service.SendChatlogs(actor, ticket.Id);

        if (!exists) {
            Assert.Empty(sent);

            return;
        }

        var actual = Assert.Single(sent);
        using var stream = (RecyclableMemoryStream)new RecyclableMemoryStreamManager().GetStream();
        var expected = new FlashOutgoingPacket(stream);
        new ModeratorTicketChatlogComposer(new(ticket.Id, ticket.Sender.Id, ticket.Reported!.Id,
            42, "current", ticket.CreatedAt, ticket.Reported.Username, ["chat"])).Compose(expected);
        Assert.Equal(ServerPacketHeader.ModeratorTicketChatlogComposer, actual.Header);
        Assert.Equal(stream.GetBuffer().AsSpan(6, checked((int)stream.Length - 6)).ToArray(), actual.Payload);
    }

    [Fact]
    public void FailedAbusePersistenceCannotCloseTicketOrSendResponse()
    {
        var ticket = Ticket();
        var manager = Proxy<IModerationManager>((method, args) => { Assert.Equal("TryGetTicket", method); args[1] = ticket; return true; });
        var (actor, sent) = HabbiconTestSupport.Client(ticket.Moderator!);
        var service = new ModeratorTicketService(manager, null!, null!, new FailingStore(), TimeProvider.System, null!);
        Assert.Throws<InvalidOperationException>(() => service.Close(actor, 10, SupportTicketResult.Abusive));
        Assert.False(ticket.Answered);
        Assert.Empty(sent);
        ticket.Moderator = null;
        service.Close(actor, 10, SupportTicketResult.Abusive); // Unassigned ticket is a no-op.
        Assert.False(ticket.Answered);
        Assert.Empty(sent);
    }

    [Fact]
    public void SubmissionFailureDoesNotPublishTicketOrBroadcast()
    {
        var manager = Proxy<IModerationManager>((method, _) => method == "UserHasTickets" ? false : throw new InvalidOperationException("unexpected publication"));
        var users = Proxy<IModeratorUserLookup>((_, _) => new Habbo { Id = 2 });
        var rooms = Proxy<IRoomDataLoader>((method, args) =>
        {
            Assert.Equal("TryGetData", method);
            args[1] = null;

            return false;
        });
        var service = new ModeratorTicketService(manager, null!, users, new FailingStore(), TimeProvider.System, rooms);
        var (actor, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        Assert.Throws<InvalidOperationException>(() => service.Submit(actor, new(" help ", 6, 2, 5, ["chat"])));
        Assert.Empty(sent);
    }

    [Fact]
    public async Task SubmissionHandlerDecodesChatsAndDiscardsTheirJunkIds()
    {
        var service = Proxy<IModeratorTicketService>((method, args) =>
        {
            Assert.Equal("Submit", method);
            var request = Assert.IsType<SubmitTicketRequest>(args[1]);
            Assert.Equal((" help ", 6, 2, 5), (request.Message, request.Category, request.ReportedUserId, request.ReportedRoomId));
            Assert.Equal(new[] { "one", "two" }, request.Chats.ToArray());

            return null;
        });
        await new SubmitNewTicketEvent(service).Parse(null!, HabbiconTestSupport.Incoming(" help ", 6, 2, 5, 2, 88, "one", 99, "two"));
    }

    [Fact]
    public async Task RoomReportDispatchPublishesSourceRoomAndTheLoadedRoom()
    {
        var (client, _, disconnected) = Connect(Person(1, "Sender", roomId: 99));
        var world = new TicketWorld();
        world.Rooms[42] = new RoomData { Id = 42 };
        await Dispatch(new SubmitNewTicketEvent(world.Service), client, ClientPacketHeader.SubmitNewTicketEvent, "help", 6, -1, 42, 0);
        var ticket = Assert.Single(world.Tickets);
        Assert.Null(ticket.Reported);
        Assert.Equal(7, ticket.Type);
        Assert.Equal(6, ticket.Category);
        Assert.Equal(42u, ticket.Room?.Id);
        Assert.NotEqual(99u, ticket.Room?.Id);
        Assert.Equal(1, world.Submissions);
        Assert.Empty(world.UserLookups);
        Assert.False(disconnected());
    }

    [Fact]
    public async Task RoomReportDispatchRefusesBeforeStoreEffects()
    {
        var problems = new List<string>();
        await Expect("non-positive room", () => RefuseRoomReport(0, assertLookup: false), problems);
        await Expect("negative room", () => RefuseRoomReport(-5, assertLookup: false), problems);
        await Expect("unresolved room", () => RefuseRoomReport(42, assertLookup: true), problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task UserReportDispatchRejectsAMissingOnlineUser()
    {
        var (client, _, disconnected) = Connect(Person(1, "Sender", roomId: 99));
        var world = new TicketWorld();
        world.Rooms[42] = new RoomData { Id = 42 };
        await Dispatch(new SubmitNewTicketEvent(world.Service), client, ClientPacketHeader.SubmitNewTicketEvent, "help", 6, 8, 42, 0);
        Assert.Equal(new[] { 8 }, world.UserLookups);
        Assert.Empty(world.RoomLookups);
        Assert.Empty(world.Tickets);
        Assert.Equal(0, world.Submissions);
        Assert.False(disconnected());
    }

    [Fact]
    public async Task UserReportDispatchAllowsANullRoomAndDoesNotUseCurrentRoom()
    {
        var problems = new List<string>();
        await Expect("non-positive room", () => SubmitUserReport(0, loaded: false, expectRoom: null), problems);
        await Expect("unresolved room", () => SubmitUserReport(42, loaded: false, expectRoom: null), problems);
        await Expect("loaded room", () => SubmitUserReport(42, loaded: true, expectRoom: 42), problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task PendingTicketDispatchReturnsBeforeLookup()
    {
        var sender = Person(1, "Sender", roomId: 99);
        var (client, sent, disconnected) = Connect(sender);
        var pending = OpenTicket(4, sender, null);
        var world = new TicketWorld { Pending = true, PendingTicket = pending };
        world.Tickets.Add(pending);
        world.Rooms[42] = new RoomData { Id = 42 };
        await Dispatch(new SubmitNewTicketEvent(world.Service), client, ClientPacketHeader.SubmitNewTicketEvent, "help", 6, -1, 42, 0);
        Assert.Equal(ServerPacketHeader.CallForHelpPendingCallsComposer, Assert.Single(sent).Header);
        Assert.Empty(world.RoomLookups);
        Assert.Empty(world.UserLookups);
        Assert.Equal(0, world.Submissions);
        Assert.Same(pending, Assert.Single(world.Tickets));
        Assert.False(pending.Answered);
        Assert.False(disconnected());
    }

    [Fact]
    public async Task CloseDispatchRejectsMalformedFramesWithoutEffects()
    {
        var problems = new List<string>();
        await Expect("resolution 4", () => RejectClose(4, 1, 11), problems);
        await Expect("resolution 0", () => RejectClose(0, 0), problems);
        await Expect("negative count", () => RejectClose(3, -1), problems);
        await Expect("truncated count", () => RejectClose(3, 2, 11), problems);
        await Expect("huge count", () => RejectClose(3, 2147483647), problems);
        await Expect("duplicate id", () => RejectClose(3, 2, 11, 11), problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task CloseDispatchCountZeroDoesNotCloseOrDisconnect()
    {
        var actor = Person(3, "Staff", moderator: true);
        var reporter = Person(1, "Ada");
        var (client, _, disconnected) = Connect(actor);
        var (reporterClient, sent, _) = Connect(reporter);
        var world = new TicketWorld();
        world.Reporters[reporter.Id] = reporterClient;
        var ticket = OpenTicket(11, reporter, actor);
        world.Tickets.Add(ticket);
        await Dispatch(new CloseTicketEvent(world.Service), client, ClientPacketHeader.CloseTicketEvent, 3, 0, 11);
        Assert.False(ticket.Answered);
        Assert.False(disconnected());
        Assert.Empty(sent);
        Assert.Equal(0, world.Abuses);
        Assert.Equal(0, world.Broadcasts);
    }

    [Fact]
    public async Task CloseDispatchStopsForAMissingModerationToolBeforeParse()
    {
        var closes = 0;
        var service = Proxy<IModeratorTicketService>((method, _) =>
        {
            closes++;
            Assert.Equal("Close", method);

            return null;
        });
        var (client, _, disconnected) = Connect(Person(3, "Staff"));
        using var manager = new PacketManager([new CloseTicketEvent(service)], NullLogger<PacketManager>.Instance);
        await manager.TryExecutePacket(client, ClientPacketHeader.CloseTicketEvent, HabbiconTestSupport.Incoming(3, 2, 11));
        Assert.False(disconnected());
        Assert.Equal(0, closes);
    }

    [Fact]
    public async Task CloseDispatchClosesEachAssignedIdAndContinuesAfterADenial()
    {
        var problems = new List<string>();
        await Expect("both assigned", CloseBothAssigned, problems);
        await Expect("denied then allowed", CloseDeniedThenAllowed, problems);
        await Expect("abusive", CloseAbusive, problems);
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static async Task RefuseRoomReport(int roomId, bool assertLookup)
    {
        var (client, _, disconnected) = Connect(Person(1, "Sender", roomId: 99));
        var world = new TicketWorld();
        await Dispatch(new SubmitNewTicketEvent(world.Service), client, ClientPacketHeader.SubmitNewTicketEvent, "help", 6, -1, roomId, 0);
        Assert.Empty(world.Tickets);
        Assert.Equal(0, world.Submissions);
        Assert.Equal(0, world.Alerts);
        Assert.Equal(0, world.Broadcasts);
        Assert.False(disconnected());
        Assert.Equal(assertLookup ? new[] { (uint)roomId } : [], world.RoomLookups);
    }

    private static async Task SubmitUserReport(int roomId, bool loaded, uint? expectRoom)
    {
        var (client, _, disconnected) = Connect(Person(1, "Sender", roomId: 99));
        var world = new TicketWorld { Reported = Person(2, "Target") };

        if (loaded) {
            world.Rooms[(uint)roomId] = new RoomData { Id = (uint)roomId };
        }

        await Dispatch(new SubmitNewTicketEvent(world.Service), client, ClientPacketHeader.SubmitNewTicketEvent, "help", 6, 2, roomId, 0);
        var ticket = Assert.Single(world.Tickets);
        Assert.Equal((1, 6, expectRoom), (ticket.Type, ticket.Category, ticket.Room?.Id));
        Assert.Equal(2, ticket.Reported?.Id);
        Assert.NotEqual(99u, ticket.Room?.Id ?? 0);
        Assert.Equal(new[] { 2 }, world.UserLookups);
        Assert.Equal(roomId > 0 ? new[] { (uint)roomId } : [], world.RoomLookups);
        Assert.False(disconnected());
    }

    private static async Task RejectClose(params int[] frame)
    {
        var actor = Person(3, "Staff", moderator: true);
        var reporter = Person(1, "Ada");
        var (client, _, disconnected) = Connect(actor);
        var (reporterClient, sent, _) = Connect(reporter);
        var world = new TicketWorld();
        world.Reporters[reporter.Id] = reporterClient;
        world.Tickets.Add(OpenTicket(11, reporter, actor));
        world.Tickets.Add(OpenTicket(12, reporter, actor));
        await Dispatch(new CloseTicketEvent(world.Service), client, ClientPacketHeader.CloseTicketEvent, frame.Cast<object>().ToArray());
        Assert.True(disconnected());
        Assert.All(world.Tickets, ticket => Assert.False(ticket.Answered));
        Assert.Equal(0, world.Abuses);
        Assert.Empty(sent);
        Assert.Equal(0, world.Broadcasts);
        Assert.Equal(0, world.Alerts);
    }

    private static async Task CloseBothAssigned()
    {
        var (world, client, adaSent, beaSent, disconnected) = AssignedPair(sameModerator: true);
        await Dispatch(new CloseTicketEvent(world.Service), client, ClientPacketHeader.CloseTicketEvent, 3, 2, 11, 12);
        Assert.True(world.Tickets.Single(ticket => ticket.Id == 11).Answered);
        Assert.True(world.Tickets.Single(ticket => ticket.Id == 12).Answered);
        Assert.Equal(ServerPacketHeader.ModeratorSupportTicketResponseComposer, Assert.Single(adaSent).Header);
        Assert.Equal(ServerPacketHeader.ModeratorSupportTicketResponseComposer, Assert.Single(beaSent).Header);
        Assert.Equal(0, world.Abuses);
        Assert.False(disconnected());
    }

    private static async Task CloseDeniedThenAllowed()
    {
        var (world, client, adaSent, beaSent, disconnected) = AssignedPair(sameModerator: false);
        await Dispatch(new CloseTicketEvent(world.Service), client, ClientPacketHeader.CloseTicketEvent, 3, 2, 11, 12);
        Assert.False(world.Tickets.Single(ticket => ticket.Id == 11).Answered);
        Assert.True(world.Tickets.Single(ticket => ticket.Id == 12).Answered);
        Assert.Empty(adaSent);
        Assert.Equal(ServerPacketHeader.ModeratorSupportTicketResponseComposer, Assert.Single(beaSent).Header);
        Assert.False(disconnected());
    }

    private static async Task CloseAbusive()
    {
        var actor = Person(3, "Staff", moderator: true);
        var ada = Person(1, "Ada");
        var (client, _, disconnected) = Connect(actor);
        var (adaClient, adaSent, _) = Connect(ada);
        var world = new TicketWorld();
        world.Reporters[ada.Id] = adaClient;
        world.Tickets.Add(OpenTicket(11, ada, actor));
        await Dispatch(new CloseTicketEvent(world.Service), client, ClientPacketHeader.CloseTicketEvent, 2, 1, 11);
        Assert.True(world.Tickets.Single().Answered);
        Assert.Equal(1, world.Abuses);
        Assert.Equal(ServerPacketHeader.ModeratorSupportTicketResponseComposer, Assert.Single(adaSent).Header);
        Assert.False(disconnected());
    }

    private static (TicketWorld World, GameClient Client, List<(uint Header, byte[] Payload)> AdaSent, List<(uint Header, byte[] Payload)> BeaSent, Func<bool> Disconnected) AssignedPair(bool sameModerator)
    {
        var actor = Person(3, "Staff", moderator: true);
        var ada = Person(1, "Ada");
        var bea = Person(2, "Bea");
        var (client, _, disconnected) = Connect(actor);
        var (adaClient, adaSent, _) = Connect(ada);
        var (beaClient, beaSent, _) = Connect(bea);
        var world = new TicketWorld();
        world.Reporters[ada.Id] = adaClient;
        world.Reporters[bea.Id] = beaClient;
        world.Tickets.Add(OpenTicket(11, ada, sameModerator ? actor : Person(9, "Other", moderator: true)));
        world.Tickets.Add(OpenTicket(12, bea, actor));

        return (world, client, adaSent, beaSent, disconnected);
    }

    private static async Task Dispatch(IPacketEvent packet, GameClient client, uint header, params object[] values)
    {
        using var manager = new PacketManager([packet], NullLogger<PacketManager>.Instance);
        await manager.TryExecutePacket(client, header, HabbiconTestSupport.Incoming(values));
    }

    private static async Task Expect(string name, Func<Task> check, List<string> problems)
    {
        try {
            await check();
        }
        catch (Exception exception) {
            problems.Add($"{name}: {exception.Message}");
        }
    }

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent, Func<bool> Disconnected) Connect(Habbo habbo)
    {
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        var disconnected = false;
        client.DisconnectRequested = () => disconnected = true;

        return (client, sent, () => disconnected);
    }

    private static Habbo Person(int id, string username, bool moderator = false, uint? roomId = null) => new()
    {
        Id = id,
        Username = username,
        Access = EditorTestSupport.Access(moderator ? [PermissionKeys.ModerationTool] : []),
        CurrentRoom = roomId is uint room ? new Room(new RoomData { Id = room }, [], TestLogging.Navigation, TestLogging.Logger, TestRoomAchievements.Unused, TestRoomOwners.Unused) : null
    };

    private static ModerationTicket OpenTicket(int id, Habbo sender, Habbo? moderator) =>
        new(id, 1, 6, DateTimeOffset.UnixEpoch, 1, sender, null, "help", null, []) { Moderator = moderator };

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);

        return packet.Writes.ToArray();
    }
    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)result).Call = call;

        return result;
    }
    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args!);
    }
    private sealed class FailingStore : IModeratorTicketStore
    {
        public void RecordSubmission(int userId) => throw new InvalidOperationException("forced failure");
        public void RecordAbuse(int userId) => throw new InvalidOperationException("forced failure");
    }

    private sealed class TicketWorld
    {
        public List<ModerationTicket> Tickets { get; } = [];
        public List<uint> RoomLookups { get; } = [];
        public List<int> UserLookups { get; } = [];
        public Dictionary<uint, RoomData> Rooms { get; } = [];
        public Dictionary<int, GameClient> Reporters { get; } = [];
        public bool Pending { get; set; }
        public ModerationTicket? PendingTicket { get; set; }
        public Habbo? Reported { get; set; }
        public int Submissions { get; private set; }
        public int Abuses { get; private set; }
        public int Alerts { get; private set; }
        public int Broadcasts { get; private set; }
        public ModeratorTicketService Service { get; }

        public TicketWorld()
        {
            Service = new(Proxy<IModerationManager>(Moderate), Proxy<IGameClientManager>(Clients), Proxy<IModeratorUserLookup>(Users),
                new RecordingStore(this), TimeProvider.System, Proxy<IRoomDataLoader>(LoadRoom));
        }

        private object? Moderate(string method, object?[] args)
        {
            switch (method) {
                case "UserHasTickets":
                    return Pending;
                case "GetTicketBySenderId":
                    return PendingTicket;
                case "TryAddTicket":
                    Tickets.Add((ModerationTicket)args[0]!);

                    return true;
                case "TryGetTicket":
                    var ticket = Tickets.FirstOrDefault(item => item.Id == (int)args[0]!);
                    args[1] = ticket;

                    return ticket != null;
                default:
                    throw new InvalidOperationException(method);
            }
        }

        private object? Users(string method, object?[] args)
        {
            if (method != "GetById") {
                throw new InvalidOperationException(method);
            }

            UserLookups.Add((int)args[0]!);

            return Reported;
        }

        private object? LoadRoom(string method, object?[] args)
        {
            if (method != "TryGetData") {
                throw new InvalidOperationException(method);
            }

            var id = (uint)args[0]!;
            RoomLookups.Add(id);
            Rooms.TryGetValue(id, out var data);
            args[1] = data;

            return data != null;
        }

        private object? Clients(string method, object?[] args)
        {
            switch (method) {
                case "ModAlert":
                    Alerts++;

                    return null;
                case "SendPacket":
                    Broadcasts++;

                    return null;
                case "GetClientByUserId":
                    Reporters.TryGetValue((int)args[0]!, out var reporter);

                    return reporter;
                default:
                    throw new InvalidOperationException(method);
            }
        }

        private sealed class RecordingStore(TicketWorld world) : IModeratorTicketStore
        {
            public void RecordSubmission(int userId) => world.Submissions++;
            public void RecordAbuse(int userId) => world.Abuses++;
        }
    }
}
