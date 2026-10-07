using System.Collections.Concurrent;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Polls;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Polls;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Rooms.Polls;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    private (RoomWordQuizComponent Quiz, RoomWordQuizService Service) WordQuiz()
    {
        Viewer();
        _client.GetHabbo().Access = UserAccess.Create([], [new(PermissionKeys.CommandWordquiz, false)]);
        var quiz = new RoomWordQuizComponent(_interactionClock);
        quiz.Initiate(_room);
        quiz.Initiated();
        Set("_components", new IRoomComponent[] { quiz });
        return (quiz, new());
    }

    private static IRoomPollService WordQuizSurveys => Proxy<IRoomPollService>((method, _) =>
        method == "Answer" ? null : throw new InvalidOperationException(method));

    private int WordQuestionId()
    {
        var reader = new FlashIncomingPacket { Buffer = _client.Packets.Last(packet => packet.Header == ServerPacketHeader.SimplePollStartComposer).Body };
        reader.ReadString();
        Assert.Equal(0, reader.ReadInt());
        return reader.ReadInt();
    }

    [Fact]
    public async Task WordQuizPublicCommandRequiresPermissionAndOwnershipAndSendsMilliseconds()
    {
        var (_, service) = WordQuiz();
        var commands = new CommandManager([new WordQuizCommand(service)], TestGameClientManager.Empty, _database, _interactionClock);
        _client.GetHabbo().Access = UserAccess.Empty;
        Assert.False(await commands.Parse(_client, ":wordquiz 2 First?"));
        Assert.Empty(_client.Sent);
        _client.GetHabbo().Access = UserAccess.Create([], [new(PermissionKeys.CommandWordquiz, false)]);
        _room.OwnerId = 8;
        Assert.False(service.Start(_client, "Foreign?", 2));
        _room.OwnerId = 7;
        Assert.True(await commands.Parse(_client, ":wordquiz 2 First?"));
        var reader = new FlashIncomingPacket { Buffer = _client.Packets.Single(packet => packet.Header == ServerPacketHeader.SimplePollStartComposer).Body };
        Assert.Equal("First?", reader.ReadString());
        Assert.Equal(0, reader.ReadInt());
        Assert.True(reader.ReadInt() < 0);
        Assert.Equal(2000, reader.ReadInt());
        Assert.False(service.Start(_client, "Replacement?", 2));
        Assert.False(service.Start(_client, "Invalid?", 0));
        Assert.False(service.Start(_client, "Invalid?", 301));
        Assert.False(service.Start(_client, new string('x', 501), 2));
        Assert.Single(_client.Sent, header => header == ServerPacketHeader.SimplePollStartComposer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WordQuizCountsOneExactBinaryVoteAndRoomTickFinalizesOnceAtDeadline(bool v2)
    {
        var (_, service) = WordQuiz();
        Assert.True(service.Start(_client, "Continue?", 2));
        var id = WordQuestionId();
        var handler = new PollAnswerEvent(WordQuizSurveys, service);
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, ":1"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "2"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id - 1, 1, "1"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(99, id, 1, "1"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 2, "0", "1"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 65));
        Assert.DoesNotContain(ServerPacketHeader.SimplePollAnswerComposer, _client.Sent);
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "1"));
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "0"));
        Assert.Single(_client.Sent, header => header == ServerPacketHeader.SimplePollAnswerComposer);
        if (v2) {
            _room.EnableV2Movement();
        }
        _interactionClock.Now = _interactionClock.Now.AddMilliseconds(1999);
        _room.ProcessRoom();
        Assert.DoesNotContain(ServerPacketHeader.SimplePollAnswersComposer, _client.Sent);
        _interactionClock.Now = _interactionClock.Now.AddMilliseconds(1);
        _room.ProcessRoom();
        _room.ProcessRoom();
        var finish = Assert.Single(_client.Packets, packet => packet.Header == ServerPacketHeader.SimplePollAnswersComposer);
        var final = new FlashIncomingPacket { Buffer = finish.Body };
        Assert.Equal(id, final.ReadInt());
        Assert.Equal(2, final.ReadInt());
        Assert.Equal("0", final.ReadString());
        Assert.Equal(0, final.ReadInt());
        Assert.Equal("1", final.ReadString());
        Assert.Equal(1, final.ReadInt());
        Assert.False(final.HasDataRemaining());
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "0"));
        Assert.Single(_client.Sent, header => header == ServerPacketHeader.SimplePollAnswerComposer);
        Assert.True(service.Start(_client, "Next?", 2));
        Assert.NotEqual(id, WordQuestionId());
        await handler.Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "0"));
        Assert.Single(_client.Sent, header => header == ServerPacketHeader.SimplePollAnswerComposer);
    }

    [Fact]
    public async Task WordQuizRejectsDetachedSessionAndVoteAtExactDeadlineWithoutCounting()
    {
        var (quiz, service) = WordQuiz();
        Assert.True(service.Start(_client, "Finish?", 1));
        var id = WordQuestionId();
        _client.GetHabbo().CurrentRoom = null;
        service.Answer(_client, 0, id, ["1"]);
        Assert.DoesNotContain(ServerPacketHeader.SimplePollAnswerComposer, _client.Sent);
        _client.GetHabbo().CurrentRoom = _room;
        _interactionClock.Now = _interactionClock.Now.AddSeconds(1);
        await new PollAnswerEvent(WordQuizSurveys, service).Parse(_client, HabbiconTestSupport.Incoming(0, id, 1, "1"));
        quiz.Cycle();
        Assert.DoesNotContain(ServerPacketHeader.SimplePollAnswerComposer, _client.Sent);
        Assert.Single(_client.Sent, header => header == ServerPacketHeader.SimplePollAnswersComposer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordQuizLateEntrantGetsTimeAndReplaysOnlyAnAdmittedRealVoter(bool departed)
    {
        var (_, service) = WordQuiz();
        Assert.True(service.Start(_client, "Opinion?", 2));
        var id = WordQuestionId();
        service.Answer(_client, 0, id, ["1"]);
        var entrant = new TestClient();
        entrant.SetHabbo(new Habbo { Id = 8, Username = "entrant", CurrentRoom = _room, Access = UserAccess.Empty });
        var actor = new RoomUser(8, RoomId, 2, _room, entrant, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(2, actor));
        if (departed) {
            Assert.True(users.TryRemove(1, out _));
            _client.GetHabbo().CurrentRoom = null;
        }
        _interactionClock.Now = _interactionClock.Now.AddMilliseconds(250);
        service.Show(entrant);
        var start = new FlashIncomingPacket { Buffer = entrant.Packets.Single(packet => packet.Header == ServerPacketHeader.SimplePollStartComposer).Body };
        start.ReadString();
        start.ReadInt();
        start.ReadInt();
        Assert.Equal(1750, start.ReadInt());
        Assert.Equal(departed ? 0 : 1, entrant.Sent.Count(header => header == ServerPacketHeader.SimplePollAnswerComposer));
        service.Answer(entrant, 0, id, ["0"]);
        var answer = new FlashIncomingPacket { Buffer = entrant.Packets.Last().Body };
        Assert.Equal(8, answer.ReadInt());
        Assert.Equal("0", answer.ReadString());
        Assert.Equal(2, answer.ReadInt());
        Assert.Equal("0", answer.ReadString());
        Assert.Equal(1, answer.ReadInt());
        Assert.Equal("1", answer.ReadString());
        Assert.Equal(1, answer.ReadInt());
        Assert.False(answer.HasDataRemaining());
    }

    [Fact]
    public void WordQuizOldDisposedRoomCannotEmitResultsOrAcceptQuestions()
    {
        var (quiz, service) = WordQuiz();
        Assert.True(service.Start(_client, "Old?", 1));
        var id = WordQuestionId();
        _client.Sent.Clear();
        _room.MDisposed = true;
        _interactionClock.Now = _interactionClock.Now.AddSeconds(1);
        quiz.Cycle();
        service.Show(_client);
        service.Answer(_client, 0, id, ["1"]);
        Assert.False(service.Start(_client, "New?", 1));
        Assert.Empty(_client.Sent);
        quiz.Dispose();
        _room.MDisposed = false;
        quiz.Cycle();
        Assert.False(service.Start(_client, "Disposed?", 1));
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task WordQuizOldAnswerPublicationPrecedesConcurrentExpiryAndRestart()
    {
        var (_, service) = WordQuiz();
        Assert.True(service.Start(_client, "Old?", 1));
        var id = WordQuestionId();
        using var sending = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var restarting = new ManualResetEventSlim();
        _client.BeforeCapture = header => {
            if (header == ServerPacketHeader.SimplePollAnswerComposer) {
                sending.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        var answer = Task.Run(() => service.Answer(_client, 0, id, ["1"]));
        Task<bool>? restart = null;
        try {
            Assert.True(sending.Wait(TimeSpan.FromSeconds(5)));
            _interactionClock.Now = _interactionClock.Now.AddSeconds(1);
            restart = Task.Run(() => {
                restarting.Set();
                return service.Start(_client, "New?", 1);
            });
            Assert.True(restarting.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(restart.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally {
            release.Set();
        }
        await answer;
        Assert.True(await restart!);
        Assert.Equal(new[] { ServerPacketHeader.SimplePollStartComposer, ServerPacketHeader.SimplePollAnswerComposer,
            ServerPacketHeader.SimplePollAnswersComposer, ServerPacketHeader.SimplePollStartComposer }, _client.Sent);
    }

    [Fact]
    public void WordQuizReentrantSendCannotReplaceQuestionOrWaitForDisposal()
    {
        var (quiz, service) = WordQuiz();
        Assert.True(service.Start(_client, "Old?", 1));
        var id = WordQuestionId();
        _client.BeforeCapture = header => {
            if (header != ServerPacketHeader.SimplePollAnswerComposer) {
                return;
            }
            _interactionClock.Now = _interactionClock.Now.AddSeconds(1);
            Assert.False(service.Start(_client, "Nested?", 1));
            using var disposed = new ManualResetEventSlim();
            var disposal = Task.Run(() => { quiz.Dispose(); disposed.Set(); });
            Assert.True(disposed.Wait(TimeSpan.FromSeconds(5)));
            disposal.GetAwaiter().GetResult();
            typeof(Plus.HabboHotel.GameClients.GameClient).GetField("_habbo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(_client, null);
            service.Show(_client);
            service.Answer(_client, 0, id, ["0"]);
        };
        service.Answer(_client, 0, id, ["1"]);
        quiz.Cycle();
        Assert.Equal(new[] { ServerPacketHeader.SimplePollStartComposer, ServerPacketHeader.SimplePollAnswerComposer }, _client.Sent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WordQuizLateEntryStartCallbackCanLeaveOrDisposeRoomWithoutReplayingVote(bool disposeRoom)
    {
        var (quiz, service) = WordQuiz();
        Assert.True(service.Start(_client, "Old?", 1));
        service.Answer(_client, 0, WordQuestionId(), ["1"]);
        var entrant = new TestClient();
        entrant.SetHabbo(new Habbo { Id = 8, Username = "entrant", CurrentRoom = _room, Access = UserAccess.Empty });
        var actor = new RoomUser(8, RoomId, 2, _room, entrant, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        Assert.True(users.TryAdd(2, actor));
        _room.MutedUsers = new();
        _room.UsersWithRights = [];
        _room.WordFilterList = [];
        Set("_tents", new Dictionary<uint, List<RoomUser>>());
        entrant.BeforeCapture = header => {
            if (header != ServerPacketHeader.SimplePollStartComposer) {
                return;
            }
            if (disposeRoom) {
                _room.Dispose();
                Assert.Null(_room.GetRoomUserManager());
            }
            else {
                _room.GetRoomUserManager().RemoveUserFromRoom(entrant, false);
            }
        };
        service.Show(entrant);
        Assert.Null(entrant.GetHabbo().CurrentRoom);
        Assert.Single(entrant.Sent, header => header == ServerPacketHeader.SimplePollStartComposer);
        Assert.DoesNotContain(ServerPacketHeader.SimplePollAnswerComposer, entrant.Sent);
        quiz.Dispose();
    }
}

public sealed class RoomWordQuizPacketTests
{
    [Fact]
    public void SimplePollPacketsMatchOfficialAirAndV75ParsersExactly()
    {
        var snapshot = new RoomWordQuizSnapshot(-7, "Opinion?", 1500, 2, 3);
        var start = new HabbiconTestSupport.RecordingPacket();
        new SimplePollStartComposer(snapshot).Compose(start);
        Assert.Equal(new object[] { "Opinion?", 0, -7, 1500, -7, 0, 3, "Opinion?" }, start.Writes);
        var answer = new HabbiconTestSupport.RecordingPacket();
        new SimplePollAnswerComposer(8, "0", 2, 3).Compose(answer);
        Assert.Equal(new object[] { 8, "0", 2, "0", 2, "1", 3 }, answer.Writes);
        var finish = new HabbiconTestSupport.RecordingPacket();
        new SimplePollAnswersComposer(-7, 2, 3).Compose(finish);
        Assert.Equal(new object[] { -7, 2, "0", 2, "1", 3 }, finish.Writes);
    }
}
