using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Xunit;
using static Plus.Tests.SnowStorm.SnowStormTestSupport;

namespace Plus.Tests.SnowStorm;

public class SnowStormLifecycleTests
{
    private readonly TestClock _clock = new();
    private readonly Store _store = new();
    private readonly SnowStormManager _manager;
    private readonly (GameClient Client, List<(uint Header, byte[] Payload)> Sent, Plus.HabboHotel.Users.Habbo Habbo) _ann = Player(1, "Ann");
    private readonly (GameClient Client, List<(uint Header, byte[] Payload)> Sent, Plus.HabboHotel.Users.Habbo Habbo) _bo = Player(2, "Bo");

    public SnowStormLifecycleTests() : this(("gamecenter.snowwar.game.length.seconds", "10")) { }

    private SnowStormLifecycleTests(params (string, string)[] extra)
    {
        _manager = Manager(_store, _clock, null, [("gamecenter.snowwar.arenas", "8"), .. extra]);
    }

    [Fact]
    public void TwoPlayersPlayAShortGameFromQuickJoinToRematch()
    {
        _manager.QuickJoin(_ann.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2GameCreatedComposer], Headers(_ann));

        Clear();
        _manager.QuickJoin(_bo.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2UserJoinedGameComposer, ServerPacketHeader.Game2StartCounterComposer], Headers(_ann));
        Assert.Equal([ServerPacketHeader.Game2GameLongDataComposer, ServerPacketHeader.Game2StartCounterComposer], Headers(_bo));
        var lobby = Read(Last(_bo, ServerPacketHeader.Game2GameLongDataComposer));
        Assert.Equal(1, lobby.ReadInt()); // game id
        Assert.Equal("Arctic Island", lobby.ReadString());
        Assert.Equal(new[] { 0, 8, 2, 8 }, new[] { lobby.ReadInt(), lobby.ReadInt(), lobby.ReadInt(), lobby.ReadInt() });
        Assert.Equal("Ann", lobby.ReadString()); // owner
        Assert.Equal(8, lobby.ReadInt());
        Assert.Equal(2, lobby.ReadInt());
        Assert.Equal(15, Read(Last(_ann, ServerPacketHeader.Game2StartCounterComposer)).ReadInt());

        Clear();
        _clock.Advance(TimeSpan.FromSeconds(15));
        _manager.Tick();
        uint[] start = [ServerPacketHeader.Game2GameStartedComposer, ServerPacketHeader.Game2EnterArenaComposer, ServerPacketHeader.Game2ArenaEnteredComposer,
            ServerPacketHeader.Game2ArenaEnteredComposer, ServerPacketHeader.Game2StageLoadComposer];
        Assert.Equal(start, Headers(_ann));
        Assert.Equal(start, Headers(_bo));
        Assert.Equal([1, 2], _store.Consumed);
        var enter = Read(Last(_ann, ServerPacketHeader.Game2EnterArenaComposer));
        Assert.Equal(new[] { 0, 8, 2, 2 }, new[] { enter.ReadInt(), enter.ReadInt(), enter.ReadInt(), enter.ReadInt() });
        Assert.Equal((1, "Ann", 1), (enter.ReadInt(), enter.ReadString(), SkipTo(enter)));
        Assert.Equal((2, "Bo", 2), (enter.ReadInt(), enter.ReadString(), SkipTo(enter)));
        Assert.Equal((50, 50), (enter.ReadInt(), enter.ReadInt()));

        Clear();
        _manager.LoadStageReady(_ann.Client);
        _manager.Tick();
        var loading = Read(Last(_bo, ServerPacketHeader.Game2StageStillLoadingComposer));
        Assert.Equal((50, 1, 1), (loading.ReadInt(), loading.ReadInt(), loading.ReadInt()));
        _manager.LoadStageReady(_bo.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2StageStillLoadingComposer, ServerPacketHeader.Game2StageStartingComposer], Headers(_bo)[1..]);
        var starting = Read(Last(_ann, ServerPacketHeader.Game2StageStartingComposer));
        Assert.Equal((0, "snowwar", 5), (starting.ReadInt(), starting.ReadString(), starting.ReadInt()));
        var humans = Humans(Last(_ann, ServerPacketHeader.Game2StageStartingComposer));
        Assert.Equal([1, 2], humans.Keys.Order());

        Clear();
        _clock.Advance(TimeSpan.FromSeconds(5));
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2StageRunningComposer, ServerPacketHeader.Game2GameStatusComposer], Headers(_ann));
        Assert.Equal(10, Read(Last(_ann, ServerPacketHeader.Game2StageRunningComposer)).ReadInt());
        Assert.Equal(0, Status(Last(_ann, ServerPacketHeader.Game2GameStatusComposer)).Turn);

        // A move stamped with an old turn lands in the first unbroadcast slot: GameStatus(1) carries it for turn 2.
        Clear();
        _manager.SetMoveTarget(_ann.Client, 26 * 3200, 20 * 3200, 0, 2);
        _clock.Advance(SnowStormGame.TurnDuration);
        _manager.Tick();
        var moved = Status(Last(_ann, ServerPacketHeader.Game2GameStatusComposer));
        Assert.Equal(1, moved.Turn);
        Assert.Contains(moved.Subturns[0], item => item.Type == 2 && item.Fields.SequenceEqual(new[] { humans[1], 26 * 3200, 20 * 3200 }));
        Assert.Equal(moved.Checksum, Status(Last(_bo, ServerPacketHeader.Game2GameStatusComposer)).Checksum);

        // A throw is the AIR pair: event 3 (at human) then event 8 (CreateSnowball) in the same subturn.
        Clear();
        _manager.ThrowAtHuman(_ann.Client, humans[2], 3, 1, 1);
        _manager.RequestFullStatus(_bo.Client, 1);
        _clock.Advance(SnowStormGame.TurnDuration);
        _manager.Tick();
        var thrown = Status(Last(_ann, ServerPacketHeader.Game2GameStatusComposer));
        Assert.Equal(2, thrown.Turn);
        var pair = thrown.Subturns[0].Where(item => item.Type is 3 or 8).ToList();
        Assert.Equal([3, 8], pair.Select(item => item.Type));
        Assert.Equal(new[] { humans[1], humans[2], 3 }, pair[0].Fields);
        Assert.Equal(humans[1], pair[1].Fields[1]);

        // The full-status requester gets FullGameStatus instead of that turn's GameStatus, with the same pending events.
        Assert.Equal([ServerPacketHeader.Game2FullGameStatusComposer], Headers(_bo));
        var full = Read(Last(_bo, ServerPacketHeader.Game2FullGameStatusComposer));
        Assert.Equal(0, full.ReadInt());
        Assert.Equal((10, 10), (full.ReadInt(), full.ReadInt()));
        SkipObjects(full);
        Assert.Equal((0, 2), (full.ReadInt(), full.ReadInt()));
        var fullStatus = ReadStatus(full);
        Assert.Equal((thrown.Turn, thrown.Checksum), (fullStatus.Turn, fullStatus.Checksum));
        Assert.Equal(thrown.Subturns.SelectMany(events => events).Select(item => item.Type), fullStatus.Subturns.SelectMany(events => events).Select(item => item.Type));
        Assert.False(full.HasDataRemaining());

        _manager.Chat(_bo.Client, "  bad throw  ");
        Clear();
        RunUntil(ServerPacketHeader.Game2GameEndingComposer);
        Assert.Contains(_ann.Sent, packet => packet.Header == ServerPacketHeader.Game2GameChatComposer);
        var chat = Read(Last(_ann, ServerPacketHeader.Game2GameChatComposer));
        Assert.Equal((2, "bobba throw"), (chat.ReadInt(), chat.ReadString()));
        var turns = _ann.Sent.Where(packet => packet.Header == ServerPacketHeader.Game2GameStatusComposer).Select(packet => Status(packet.Payload).Turn).ToList();
        Assert.Equal(Enumerable.Range(3, 63), turns); // 10 s = 66 turns of 150 ms
        Assert.Equal(0, Read(Last(_ann, ServerPacketHeader.Game2StageEndingComposer)).ReadInt());
        var ending = Read(Last(_bo, ServerPacketHeader.Game2GameEndingComposer));
        Assert.Equal(30, ending.ReadInt());
        Assert.False(ending.ReadBool());
        ending.ReadInt();
        ending.ReadInt();
        Assert.Equal(2, ending.ReadInt());
        Assert.Equal([1, 2], _store.Recorded.Select(score => score.UserId));
        Assert.All(_store.Recorded, score => Assert.Equal(new DateOnly(2026, 10, 5), score.Week));

        Clear();
        _manager.PlayAgain(_ann.Client);
        _manager.Tick();
        Assert.Equal(1, Read(Last(_bo, ServerPacketHeader.Game2PlayerRematchesComposer)).ReadInt());

        _clock.Advance(TimeSpan.FromSeconds(30));
        _manager.Tick();
        Assert.Equal(-1, Read(Last(_bo, ServerPacketHeader.Game2RejoinPreviousRoomComposer)).ReadInt());
        Assert.DoesNotContain(_bo.Sent, packet => packet.Header == ServerPacketHeader.Game2GameCreatedComposer);
        Assert.Equal([ServerPacketHeader.Game2PlayerRematchesComposer, ServerPacketHeader.Game2RejoinPreviousRoomComposer, ServerPacketHeader.Game2GameCreatedComposer],
            Headers(_ann));
        var rematch = Read(Last(_ann, ServerPacketHeader.Game2GameCreatedComposer));
        Assert.NotEqual(1, rematch.ReadInt());
    }

    [Fact]
    public void LeavingARunningGameBlocksTheLeaverAndEndsTheGameForTheLastPlayer()
    {
        var humans = StartRunningGame();
        Clear();
        _manager.ExitGame(_ann.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2UserBlockedComposer, ServerPacketHeader.Game2RejoinPreviousRoomComposer], Headers(_ann));
        Assert.Equal(180, Read(_ann.Sent[0].Payload).ReadInt());
        Assert.Equal(180, _manager.BlockSeconds(1));
        var exited = Read(Last(_bo, ServerPacketHeader.Game2PlayerExitedGameArenaComposer));
        Assert.Equal((1, humans[1]), (exited.ReadInt(), exited.ReadInt()));
        Assert.Contains(ServerPacketHeader.Game2GameEndingComposer, Headers(_bo));

        Clear();
        _manager.QuickJoin(_ann.Client);
        Assert.Equal(ServerPacketHeader.Game2UserBlockedComposer, Assert.Single(_ann.Sent).Header);
        _clock.Advance(TimeSpan.FromSeconds(180));
        Assert.Equal(0, _manager.BlockSeconds(1));
    }

    [Fact]
    public void HumanLeftGameIsBroadcastWhenThreePlayersRemainAndOneLeaves()
    {
        var cy = Player(3, "Cy");
        _manager.QuickJoin(_ann.Client);
        _manager.QuickJoin(_bo.Client);
        _manager.QuickJoin(cy.Client);
        _manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        _manager.Tick();
        _clock.Advance(SnowStormGame.LoadTimeout);
        _manager.Tick();
        var humans = Humans(Last(_ann, ServerPacketHeader.Game2StageStartingComposer));
        _clock.Advance(TimeSpan.FromSeconds(5));
        _manager.Tick();
        Clear();
        cy.Sent.Clear();
        _manager.ExitGame(cy.Client);
        _manager.Tick();
        _clock.Advance(SnowStormGame.TurnDuration);
        _manager.Tick();
        var status = _ann.Sent.Where(packet => packet.Header == ServerPacketHeader.Game2GameStatusComposer).Select(packet => Status(packet.Payload)).ToList();
        Assert.Contains(status, item => item.Subturns.Any(events => events.Any(evt => evt.Type == 1 && evt.Fields[0] == humans[3])));
        Assert.DoesNotContain(cy.Sent, packet => packet.Header == ServerPacketHeader.Game2GameStatusComposer);
    }

    [Fact]
    public void LobbyCountdownStopsWhenAPlayerLeavesAndDisconnectsLeaveTheLobby()
    {
        _manager.QuickJoin(_ann.Client);
        _manager.QuickJoin(_bo.Client);
        _manager.Tick();
        Clear();
        _manager.LeaveLobby(_bo.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2UserLeftGameComposer, ServerPacketHeader.Game2StopCounterComposer], Headers(_ann));
        Assert.Equal(2, Read(_ann.Sent[0].Payload).ReadInt());

        Clear();
        _ann.Habbo.Dispose();
        _manager.Tick();
        _manager.QuickJoin(_bo.Client);
        _manager.Tick();
        Assert.Equal([ServerPacketHeader.Game2GameCreatedComposer], Headers(_bo));
        Assert.Empty(_ann.Sent);
    }

    [Fact]
    public void JoinIsRefusedWithoutGamesLeftOrWhenDisabled()
    {
        _store.Accounts[1] = new SnowStormAccount(4, 10, 0);
        _manager.QuickJoin(_ann.Client);
        Assert.Equal(8, Read(Assert.Single(_ann.Sent).Payload).ReadInt());

        var disabled = new SnowStormManager(_store, Arenas(), new Settings(), new Filter(), _clock, NullLogger<SnowStormManager>.Instance);
        disabled.QuickJoin(_bo.Client);
        var failed = Assert.Single(_bo.Sent);
        Assert.Equal(ServerPacketHeader.Game2JoiningGameFailedComposer, failed.Header);
        Assert.Equal(1, Read(failed.Payload).ReadInt());
    }

    [Fact]
    public void FullLobbiesWaitInTheArenaQueueWhileTheOnlySlotIsBusy()
    {
        var manager = Manager(_store, _clock, null, ("gamecenter.snowwar.queue.match.max", "2"));
        var players = Enumerable.Range(1, 4).Select(id => Player(id, "P" + id)).ToList();

        foreach (var player in players) {
            manager.QuickJoin(player.Client);
        }

        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        Assert.Contains(players[0].Sent, packet => packet.Header == ServerPacketHeader.Game2GameStartedComposer);
        Assert.DoesNotContain(players[2].Sent, packet => packet.Header == ServerPacketHeader.Game2GameStartedComposer);
        Assert.Equal(1, Read(players[2].Sent.Last(packet => packet.Header == ServerPacketHeader.Game2InArenaQueueComposer).Payload).ReadInt());
        // The fourth player joined the second lobby, on the other team than the third.
        var second = Read(players[3].Sent.First(packet => packet.Header == ServerPacketHeader.Game2GameLongDataComposer).Payload);
        Assert.Equal(2, second.ReadInt());
        var joined = Read(players[2].Sent.First(packet => packet.Header == ServerPacketHeader.Game2UserJoinedGameComposer).Payload);
        Assert.Equal((4, "P4"), (joined.ReadInt(), joined.ReadString()));
        joined.ReadString();
        Assert.Equal(("M", 2), (joined.ReadString(), joined.ReadInt()));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void RayGunBurstsWhenAPlayerStopsOnItsUseTileUnlessDisabled(string? setting, bool fires)
    {
        (string, string)[] extra = setting == null ? [] : [("gamecenter.snowwar.raygun.enabled", setting)];
        var manager = Manager(_store, _clock, null, [("gamecenter.snowwar.arenas", "8"), ("gamecenter.snowwar.game.length.seconds", "30"), .. extra]);
        manager.QuickJoin(_ann.Client);
        manager.QuickJoin(_bo.Client);
        manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        manager.Tick();
        manager.LoadStageReady(_ann.Client);
        manager.LoadStageReady(_bo.Client);
        manager.Tick();
        var humans = Humans(Last(_ann, ServerPacketHeader.Game2StageStartingComposer));
        _clock.Advance(TimeSpan.FromSeconds(5));
        manager.Tick();

        // Arctic Island's gun "28 12 4" (fuse object 97) faces south; Ann's north team zone walks straight onto (28, 11).
        manager.SetMoveTarget(_ann.Client, 28 * 3200, 11 * 3200, 0, 0);

        for (var turn = 0; turn < 60; turn++) {
            _clock.Advance(SnowStormGame.TurnDuration);
            manager.Tick();
        }

        var bursts = _bo.Sent.Where(packet => packet.Header == ServerPacketHeader.Game2GameStatusComposer)
            .SelectMany(packet => Status(packet.Payload).Subturns.SelectMany(events => events)).Where(item => item.Type == 100).ToList();

        if (!fires) {
            Assert.Empty(bursts);

            return;
        }

        var burst = Assert.Single(bursts);
        Assert.Equal(humans[1], burst.Fields[0]);
        Assert.Equal(97, burst.Fields[1]);
        Assert.True(burst.Fields[2] > humans.Values.Max());
    }

    private Dictionary<int, int> StartRunningGame()
    {
        _manager.QuickJoin(_ann.Client);
        _manager.QuickJoin(_bo.Client);
        _manager.Tick();
        _clock.Advance(TimeSpan.FromSeconds(15));
        _manager.Tick();
        _manager.LoadStageReady(_ann.Client);
        _manager.LoadStageReady(_bo.Client);
        _manager.Tick();
        var humans = Humans(Last(_ann, ServerPacketHeader.Game2StageStartingComposer));
        _clock.Advance(TimeSpan.FromSeconds(5));
        _manager.Tick();

        return humans;
    }

    // userId -> human object id from the StageStarting objects (the human's userId is its last int).
    private static Dictionary<int, int> Humans(byte[] payload)
    {
        var packet = Read(payload);
        packet.ReadInt();
        packet.ReadString();
        packet.ReadInt();
        var humans = new Dictionary<int, int>();
        var count = packet.ReadInt();

        for (var index = 0; index < count; index++) {
            var type = packet.ReadInt();
            var values = Enumerable.Range(0, type switch { 1 => 10, 2 => 8, 3 => 6, 4 => 7, _ => 18 }).Select(_ => packet.ReadInt()).ToArray();

            if (type == 5) {
                humans[values[^1]] = values[0];

                for (var text = 0; text < 4; text++) {
                    packet.ReadString();
                }
            }
        }

        return humans;
    }

    private void RunUntil(uint header)
    {
        for (var turn = 0; turn < 100 && !_ann.Sent.Any(packet => packet.Header == header); turn++) {
            _clock.Advance(SnowStormGame.TurnDuration);
            _manager.Tick();
        }
    }

    private void Clear()
    {
        _ann.Sent.Clear();
        _bo.Sent.Clear();
    }

    private static uint[] Headers((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Plus.HabboHotel.Users.Habbo Habbo) player) =>
        player.Sent.Select(packet => packet.Header).ToArray();

    private static byte[] Last((GameClient Client, List<(uint Header, byte[] Payload)> Sent, Plus.HabboHotel.Users.Habbo Habbo) player, uint header) =>
        player.Sent.Last(packet => packet.Header == header).Payload;

    // Reads the rest of a Game2PlayerData after id and name: figure, gender, then returns the team.
    private static int SkipTo(Plus.Communication.Flash.FlashIncomingPacket packet)
    {
        packet.ReadString();
        packet.ReadString();

        return packet.ReadInt();
    }

    private static void SkipObjects(Plus.Communication.Flash.FlashIncomingPacket packet)
    {
        var count = packet.ReadInt();

        for (var index = 0; index < count; index++) {
            var type = packet.ReadInt();
            var ints = type switch { 1 => 10, 2 => 8, 3 => 6, 4 => 7, _ => 18 };

            for (var value = 0; value < ints; value++) {
                packet.ReadInt();
            }

            if (type == 5) {
                for (var text = 0; text < 4; text++) {
                    packet.ReadString();
                }
            }
        }
    }

    private static (int Turn, int Checksum, List<List<(int Type, int[] Fields)>> Subturns) Status(byte[] payload) => ReadStatus(Read(payload));

    private static (int Turn, int Checksum, List<List<(int Type, int[] Fields)>> Subturns) ReadStatus(Plus.Communication.Flash.FlashIncomingPacket packet)
    {
        var turn = packet.ReadInt();
        var checksum = packet.ReadInt();
        var subturns = new List<List<(int, int[])>>();
        var count = packet.ReadInt();

        for (var subturn = 0; subturn < count; subturn++) {
            var events = new List<(int, int[])>();
            var eventCount = packet.ReadInt();

            for (var index = 0; index < eventCount; index++) {
                var type = packet.ReadInt();
                var size = type switch { 1 => 1, 2 => 3, 3 => 3, 4 => 4, 7 => 1, 8 => 5, 11 => 1, 12 => 2, 100 => 3, _ => throw new InvalidDataException($"event {type}") };
                events.Add((type, Enumerable.Range(0, size).Select(_ => packet.ReadInt()).ToArray()));
            }

            subturns.Add(events);
        }

        return (turn, checksum, subturns);
    }
}
