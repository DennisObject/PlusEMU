using System.Collections.Immutable;
using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Moderation;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
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
        ticket.Sender.Username = "changed"; ticket.Moderator = null; ticket.Issue = "changed"; ticket.Answered = true;
        var expectedSupport = new object[] { 10, 2, 5, 6, 4000, 4, 0, 1, "Sender", 2, "Target", 3, "Staff", "help", (uint)0, 0 };
        var expectedInit = new object[] { 1, 10, 2, 5, 6, 4000, 4, 1, 1, "Sender", 2, "Target", 3, "Staff", "help", (uint)0, 0,
            1, "warn", 0, true, true, true, true, true, true, true, 1, "room" };
        Assert.Equal(expectedSupport, Compose(support)); Assert.Equal(expectedSupport, Compose(support));
        Assert.Equal(expectedInit, Compose(init)); Assert.Equal(expectedInit, Compose(init));
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
        var ticket = Ticket(); ticket.CreatedAt = Now.AddSeconds(-seconds);
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

        if (!exists) { Assert.Empty(sent); return; }
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
        Assert.False(ticket.Answered); Assert.Empty(sent);
        ticket.Moderator = null;
        service.Close(actor, 10, SupportTicketResult.Abusive); // Unassigned ticket is a no-op.
        Assert.False(ticket.Answered); Assert.Empty(sent);
    }

    [Fact]
    public void SubmissionFailureDoesNotPublishTicketOrBroadcast()
    {
        var manager = Proxy<IModerationManager>((method, _) => method == "UserHasTickets" ? false : throw new InvalidOperationException("unexpected publication"));
        var users = Proxy<IModeratorUserLookup>((_, _) => new Habbo { Id = 2 });
        var service = new ModeratorTicketService(manager, null!, users, new FailingStore(), TimeProvider.System, null!);
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
            Assert.Equal((" help ", 6, 2, 5), (request.Message, request.Category, request.ReportedUserId, request.Type));
            Assert.Equal(new[] { "one", "two" }, request.Chats.ToArray());
            return null;
        });
        await new SubmitNewTicketEvent(service).Parse(null!, HabbiconTestSupport.Incoming(" help ", 6, 2, 5, 2, 88, "one", 99, "two"));
    }

    private static object[] Compose(IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket(); composer.Compose(packet); return packet.Writes.ToArray();
    }
    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var result = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)result).Call = call; return result;
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
}
