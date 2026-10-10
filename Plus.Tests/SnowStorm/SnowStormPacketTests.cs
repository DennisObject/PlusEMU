using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Incoming.Game.Arena;
using Plus.Communication.Packets.Incoming.Game.Directory;
using Plus.Communication.Packets.Incoming.Game.Ingame;
using Plus.Communication.Packets.Incoming.Game.Lobby;
using Plus.Communication.Packets.Incoming.Game.Score;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Game;
using Plus.Communication.Packets.Outgoing.Game.SnowStorm;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games.SnowStorm;
using Plus.HabboHotel.Games.SnowStorm.Simulation;
using Xunit;
using static Plus.Tests.HabbiconTestSupport;

namespace Plus.Tests.SnowStorm;

public class SnowStormPacketTests
{
    private static readonly Dictionary<string, uint> Incoming = new()
    {
        ["Game2CheckGameDirectoryStatusEvent"] = ClientPacketHeader.Game2CheckGameDirectoryStatusEvent,
        ["Game2GetAccountGameStatusEvent"] = ClientPacketHeader.Game2GetAccountGameStatusEvent,
        ["Game2QuickJoinEvent"] = ClientPacketHeader.Game2QuickJoinEvent,
        ["Game2LeaveLobbyEvent"] = ClientPacketHeader.Game2LeaveLobbyEvent,
        ["Game2LoadStageReadyEvent"] = ClientPacketHeader.Game2LoadStageReadyEvent,
        ["Game2ExitGameEvent"] = ClientPacketHeader.Game2ExitGameEvent,
        ["Game2GameChatEvent"] = ClientPacketHeader.Game2GameChatEvent,
        ["Game2PlayAgainEvent"] = ClientPacketHeader.Game2PlayAgainEvent,
        ["Game2SetUserMoveTargetEvent"] = ClientPacketHeader.Game2SetUserMoveTargetEvent,
        ["Game2ThrowSnowballAtPositionEvent"] = ClientPacketHeader.Game2ThrowSnowballAtPositionEvent,
        ["Game2ThrowSnowballAtHumanEvent"] = ClientPacketHeader.Game2ThrowSnowballAtHumanEvent,
        ["Game2MakeSnowballEvent"] = ClientPacketHeader.Game2MakeSnowballEvent,
        ["Game2RequestFullStatusUpdateEvent"] = ClientPacketHeader.Game2RequestFullStatusUpdateEvent,
        ["Game2GetTotalLeaderboardEvent"] = ClientPacketHeader.Game2GetTotalLeaderboardEvent,
        ["Game2GetFriendsLeaderboardEvent"] = ClientPacketHeader.Game2GetFriendsLeaderboardEvent,
        ["Game2GetWeeklyLeaderboardEvent"] = ClientPacketHeader.Game2GetWeeklyLeaderboardEvent,
        ["Game2GetWeeklyFriendsLeaderboardEvent"] = ClientPacketHeader.Game2GetWeeklyFriendsLeaderboardEvent,
        ["Game2GetTotalGroupLeaderboardEvent"] = ClientPacketHeader.Game2GetTotalGroupLeaderboardEvent,
        ["Game2GetWeeklyGroupLeaderboardEvent"] = ClientPacketHeader.Game2GetWeeklyGroupLeaderboardEvent,
        ["GetSnowWarGameTokensOfferEvent"] = ClientPacketHeader.GetSnowWarGameTokensOfferEvent,
        ["PurchaseSnowWarGameTokensOfferEvent"] = ClientPacketHeader.PurchaseSnowWarGameTokensOfferEvent
    };

    private static readonly Dictionary<string, uint> Outgoing = new()
    {
        ["Game2GameDirectoryStatusComposer"] = ServerPacketHeader.Game2GameDirectoryStatusComposer,
        ["GameAccountStatusComposer"] = ServerPacketHeader.GameAccountStatusComposer,
        ["Game2GameCreatedComposer"] = ServerPacketHeader.Game2GameCreatedComposer,
        ["Game2InArenaQueueComposer"] = ServerPacketHeader.Game2InArenaQueueComposer,
        ["Game2GameLongDataComposer"] = ServerPacketHeader.Game2GameLongDataComposer,
        ["Game2StartCounterComposer"] = ServerPacketHeader.Game2StartCounterComposer,
        ["Game2UserJoinedGameComposer"] = ServerPacketHeader.Game2UserJoinedGameComposer,
        ["Game2UserLeftGameComposer"] = ServerPacketHeader.Game2UserLeftGameComposer,
        ["Game2StopCounterComposer"] = ServerPacketHeader.Game2StopCounterComposer,
        ["Game2GameStartedComposer"] = ServerPacketHeader.Game2GameStartedComposer,
        ["Game2EnterArenaComposer"] = ServerPacketHeader.Game2EnterArenaComposer,
        ["Game2ArenaEnteredComposer"] = ServerPacketHeader.Game2ArenaEnteredComposer,
        ["Game2EnterArenaFailedComposer"] = ServerPacketHeader.Game2EnterArenaFailedComposer,
        ["Game2GameStatusComposer"] = ServerPacketHeader.Game2GameStatusComposer,
        ["Game2FullGameStatusComposer"] = ServerPacketHeader.Game2FullGameStatusComposer,
        ["Game2StageStartingComposer"] = ServerPacketHeader.Game2StageStartingComposer,
        ["Game2StageLoadComposer"] = ServerPacketHeader.Game2StageLoadComposer,
        ["Game2RejoinPreviousRoomComposer"] = ServerPacketHeader.Game2RejoinPreviousRoomComposer,
        ["Game2StageStillLoadingComposer"] = ServerPacketHeader.Game2StageStillLoadingComposer,
        ["Game2GameEndingComposer"] = ServerPacketHeader.Game2GameEndingComposer,
        ["Game2GameChatComposer"] = ServerPacketHeader.Game2GameChatComposer,
        ["Game2StageRunningComposer"] = ServerPacketHeader.Game2StageRunningComposer,
        ["Game2StageEndingComposer"] = ServerPacketHeader.Game2StageEndingComposer,
        ["Game2PlayerExitedGameArenaComposer"] = ServerPacketHeader.Game2PlayerExitedGameArenaComposer,
        ["Game2PlayerRematchesComposer"] = ServerPacketHeader.Game2PlayerRematchesComposer,
        ["Game2JoiningGameFailedComposer"] = ServerPacketHeader.Game2JoiningGameFailedComposer,
        ["Game2StartingGameFailedComposer"] = ServerPacketHeader.Game2StartingGameFailedComposer,
        ["Game2GameCancelledComposer"] = ServerPacketHeader.Game2GameCancelledComposer,
        ["Game2GameNotFoundComposer"] = ServerPacketHeader.Game2GameNotFoundComposer,
        ["Game2UserBlockedComposer"] = ServerPacketHeader.Game2UserBlockedComposer,
        ["Game2TotalLeaderboardComposer"] = ServerPacketHeader.Game2TotalLeaderboardComposer,
        ["Game2FriendsLeaderboardComposer"] = ServerPacketHeader.Game2FriendsLeaderboardComposer,
        ["Game2WeeklyLeaderboardComposer"] = ServerPacketHeader.Game2WeeklyLeaderboardComposer,
        ["Game2WeeklyFriendsLeaderboardComposer"] = ServerPacketHeader.Game2WeeklyFriendsLeaderboardComposer,
        ["Game2TotalGroupLeaderboardComposer"] = ServerPacketHeader.Game2TotalGroupLeaderboardComposer,
        ["Game2WeeklyGroupLeaderboardComposer"] = ServerPacketHeader.Game2WeeklyGroupLeaderboardComposer,
        ["SnowWarGameTokensComposer"] = ServerPacketHeader.SnowWarGameTokensComposer
    };

    [Fact]
    public void EveryHeaderIsMappedToItsOctaneIdInEveryRevisionWithoutCollisions()
    {
        foreach (var file in Directory.GetFiles(HabbiconPacketTests.Repo("Resources/Revisions"), "*.json")) {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            var revision = new Plus.Communication.Revisions.RevisionsCache().InternalRevision;

            foreach (var (key, expected, type) in new[] { ("IncomingHeaders", Incoming, typeof(ClientPacketHeader)), ("OutgoingHeaders", Outgoing, typeof(ServerPacketHeader)) }) {
                var section = json.RootElement.GetProperty(key);

                foreach (var (name, id) in expected) {
                    var constant = (uint)type.GetField(name)!.GetRawConstantValue()!;
                    var wire = section.GetProperty(name).GetUInt32();

                    Assert.Equal(id, wire);

                    if (key == "IncomingHeaders") {
                        Assert.Equal(constant, revision.IncomingIdToInternalIdMapping[wire]);
                    }
                    else {
                        Assert.Equal(wire, revision.InternalIdToOutgoingIdMapping[constant]);
                    }
                }

                var ids = section.EnumerateObject().Select(header => header.Value.GetUInt32()).Where(id => id > 0).ToList();
                Assert.Equal(ids.Count, ids.Distinct().Count());
            }
        }

        foreach (var type in new[] { typeof(ClientPacketHeader), typeof(ServerPacketHeader) }) {
            var ids = type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (uint)field.GetRawConstantValue()!).Where(id => id > 0).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
        }

        // Every incoming name has a handler class of the same name, and every outgoing name a composer.
        var types = typeof(SnowStormManager).Assembly.GetTypes();
        Assert.All(Incoming.Keys, name => Assert.Contains(types, type => type.Name == name && typeof(IPacketEvent).IsAssignableFrom(type)));
        Assert.All(Outgoing.Keys, name => Assert.Contains(types, type => type.Name == name && typeof(IServerPacket).IsAssignableFrom(type)));
    }

    [Fact]
    public void LobbyComposersWriteGameLobbyData()
    {
        var player = new SnowStormLobbyPlayer(4, "Ann", "hd-1", "F", 2, 3, 120, 80);
        var lobby = new SnowStormLobbySnapshot(7, "Arctic Island", 0, 8, 2, 8, "Ann", 8, [player]);
        object[] lobbyWire = [7, "Arctic Island", 0, 8, 2, 8, "Ann", 8, 1, 4, "Ann", "hd-1", "F", 2, 3, 120, 80];
        Assert.Equal(lobbyWire, Write(new Game2GameCreatedComposer(lobby)));
        Assert.Equal(lobbyWire, Write(new Game2GameLongDataComposer(lobby)));
        Assert.Equal(lobbyWire, Write(new Game2GameStartedComposer(lobby)));
        Assert.Equal(new object[] { 4, "Ann", "hd-1", "F", 2, 3, 120, 80, true }, Write(new Game2UserJoinedGameComposer(player, true)));
        Assert.Equal(new object[] { 0 }, Write(new Game2StopCounterComposer()));
        Assert.Empty(Write(new Game2GameCancelledComposer()));
        Assert.Equal(new object[] { 0, 180, 4, -1 }, Write(new Game2GameDirectoryStatusComposer(0, 180, 4, -1)));
        Assert.Equal(new object[] { 0, 9, 12 }, Write(new GameAccountStatusComposer(0, 9, 12)));
        Assert.Equal(new object[] { 3, -1, 0 }, Write(new GameAccountStatusComposer(3)));
    }

    [Fact]
    public void ArenaComposersWriteAirStructures()
    {
        var level = new SnowStormLevelData(2, 1, "00", [new SnowStormFuseObject("snst_tree1", 1, 1, 0, 1, 1, 3200, 0, 1440, false, "0")]);
        Assert.Equal(new object[] { 0, 8, 2, 1, 4, "Ann", "hd-1", "F", 1, 2, 1, "00", 1, "snst_tree1", 1, 1, 0, 1, 1, 3200, 0, 1440, false, 0, "0" },
            Write(new Game2EnterArenaComposer(0, 8, 2, [new SnowStormArenaPlayer(4, "Ann", "hd-1", "F", 1)],
                new SnowStormArenaLevel(level, ImmutableDictionary<int, ImmutableArray<KeyValuePair<string, string>>>.Empty))));
        var backdrop = SnowStormArenas.ForGame(new SnowStormArenaDefinition(8, "Arctic Island", level, new Dictionary<int, IReadOnlyList<(int X, int Y)>>(),
            new SnowStormBackdrop(0, 0, -1160, 1554, 10000)), "/bg.png");
        Assert.Equal(new object[] { 0, 8, 2, 0, 2, 1, "00", 2, "snst_tree1", 1, 1, 0, 1, 1, 3200, 0, 1440, false, 0, "0",
                "ads_background", 2, 0, 0, 1, 1, 0, 1, 0, true, 1, 5, "state", "0", "imageUrl", "/bg.png", "offsetX", "-1160", "offsetY", "1554", "offsetZ", "10000" },
            Write(new Game2EnterArenaComposer(0, 8, 2, [], backdrop)));
        Assert.Equal(new object[] { 50, 2, 4, 5 }, Write(new Game2StageStillLoadingComposer(50, [4, 5])));

        var human = new SnowStormWireObject([5, 20, 1, 2], ["Ann", "", "hd-1", "F"]);
        Assert.Equal(new object[] { 0, "snowwar", 5, 1, 5, 20, 1, 2, "Ann", "", "hd-1", "F" },
            Write(new Game2StageStartingComposer(0, "snowwar", 5, [human])));

        var status = new SnowStormStatusSnapshot(9, -77, [[new SnowStormWireEvent(2, [20, 3200, 6400])], [], [new SnowStormWireEvent(11, [3])]]);
        object[] statusWire = [9, -77, 3, 1, 2, 20, 3200, 6400, 0, 1, 11, 3];
        Assert.Equal(statusWire, Write(new Game2GameStatusComposer(status)));
        Assert.Equal(new object[] { 0, 170, 180, 1, 5, 20, 1, 2, "Ann", "", "hd-1", "F", 0, 2 }.Concat(statusWire),
            Write(new Game2FullGameStatusComposer(new SnowStormFullStatusSnapshot(170, 180, [human], 2, status))));

        var stats = new SnowStormPlayerStatsSnapshot(6, 1, 0, 1, 0, 2, 1, 0, 0, 0);
        var result = new SnowStormGameResult(false, SnowStormGameResult.Win, 1,
            [new SnowStormTeamResult(1, 6, [new SnowStormTeamPlayerResult("Ann", 4, "hd-1", "F", 6, stats)]), new SnowStormTeamResult(2, 0, [])], 4, 4);
        Assert.Equal(new object[] { 30, false, 1, 1, 2, 1, 6, 1, "Ann", 4, "hd-1", "F", 6, 6, 1, 0, 1, 0, 2, 1, 0, 0, 0, 2, 0, 0, 4, 4 },
            Write(new Game2GameEndingComposer(30, result)));
        Assert.Equal(new object[] { 4, 20 }, Write(new Game2PlayerExitedGameArenaComposer(4, 20)));
        Assert.Equal(new object[] { 4, "hi" }, Write(new Game2GameChatComposer(4, "hi")));
        Assert.Equal(new object[] { 12 }, Write(new Game2RejoinPreviousRoomComposer(12)));
    }

    [Fact]
    public void LeaderboardAndTokenComposersWriteAirPayloads()
    {
        var entry = new SnowStormLeaderboardEntry(4, 50, 1, "Ann", "hd-1", "f");
        var total = new SnowStormLeaderboardPage([entry], 9, 0);
        object[] baseWire = [1, 4, 50, 1, "Ann", "hd-1", "f", 9, 0];
        Assert.Equal(baseWire, Write(new Game2TotalLeaderboardComposer(total)));
        Assert.Equal(baseWire, Write(new Game2FriendsLeaderboardComposer(total)));
        Assert.Equal(baseWire.Append(3), Write(new Game2TotalGroupLeaderboardComposer(total with { FavouriteGroupId = 3 })));

        var weekly = total with { Week = new SnowStormLeaderboardWeek(2026, 41, 2, 1, 600) };
        object[] weeklyWire = [2026, 41, 2, 1, 600, .. baseWire];
        Assert.Equal(weeklyWire, Write(new Game2WeeklyLeaderboardComposer(weekly)));
        Assert.Equal(weeklyWire, Write(new Game2WeeklyFriendsLeaderboardComposer(weekly)));
        Assert.Equal(weeklyWire.Append(3), Write(new Game2WeeklyGroupLeaderboardComposer(weekly with { FavouriteGroupId = 3 })));
        Assert.Equal(new object[] { 1, 2, "GET_SNOWWAR_TOKENS", 10, 0, 0 }, Write(new SnowWarGameTokensComposer([new SnowStormTokenOffer(2, "GET_SNOWWAR_TOKENS", 10, 0, 0, 10)])));
    }

    [Fact]
    public async Task HandlersDelegateValidPayloadsAndDropTruncatedOrTrailingOnes()
    {
        var manager = new RecordingManager();
        var directory = new RecordingDirectory();
        var (client, _) = Client(new Plus.HabboHotel.Users.Habbo { Id = 1 });
        (IPacketEvent Handler, object[] Valid, string Expected)[] cases =
        [
            (new Game2QuickJoinEvent(manager), [], "join"),
            (new Game2LeaveLobbyEvent(manager), [], "leave"),
            (new Game2LoadStageReadyEvent(manager), [100], "ready"),
            (new Game2ExitGameEvent(manager), [true], "exit"),
            (new Game2PlayAgainEvent(manager), [], "again"),
            (new Game2GameChatEvent(manager), ["hi"], "chat hi"),
            (new Game2SetUserMoveTargetEvent(manager), [3200, 6400, 7, 2], "move 3200 6400 7 2"),
            (new Game2ThrowSnowballAtPositionEvent(manager), [3200, 6400, 1, 7, 2], "throw 3200 6400 1 7 2"),
            (new Game2ThrowSnowballAtHumanEvent(manager), [20, 3, 7, 2], "hit 20 3 7 2"),
            (new Game2MakeSnowballEvent(manager), [7, 2], "make 7 2"),
            (new Game2RequestFullStatusUpdateEvent(manager), [1], "full 1"),
            (new Game2CheckGameDirectoryStatusEvent(directory), [], "directory"),
            (new Game2GetAccountGameStatusEvent(directory), [0], "account 0"),
            (new Game2GetTotalLeaderboardEvent(directory), [0, -1, 0, 8, 50], "Total 0 0 -1 8 50"),
            (new Game2GetWeeklyLeaderboardEvent(directory), [0, 1, -1, 0, 8, 50], "Weekly 0 1 -1 8 50"),
            (new Game2GetWeeklyGroupLeaderboardEvent(directory), [0, 1, 5, 1, 8, 50], "WeeklyGroup 0 1 5 8 50"),
            (new GetSnowWarGameTokensOfferEvent(directory), [], "offers"),
            (new PurchaseSnowWarGameTokensOfferEvent(directory), [2], "buy 2")
        ];

        foreach (var (handler, valid, expected) in cases) {
            manager.Calls.Clear();
            directory.Calls.Clear();
            await handler.Parse(client, Incoming(valid));
            Assert.Equal(expected, Assert.Single(manager.Calls.Concat(directory.Calls)));
            manager.Calls.Clear();
            directory.Calls.Clear();
            await handler.Parse(client, Incoming([.. valid, 99]));

            if (valid.Length > 0 && valid[^1] is int) {
                await handler.Parse(client, Incoming(valid[..^1]));
            }

            Assert.Empty(manager.Calls.Concat(directory.Calls));
        }

        // ExitGame's flag is optional; LoadStageReady only accepts a percentage.
        await new Game2ExitGameEvent(manager).Parse(client, Incoming());
        await new Game2LoadStageReadyEvent(manager).Parse(client, Incoming(101));
        Assert.Equal(["exit"], manager.Calls);
    }

    [Fact]
    public void DirectoryReportsAvailabilityGamesLeftAndRejectsFailedPurchases()
    {
        var store = new SnowStormTestSupport.Store();
        store.Accounts[1] = new SnowStormAccount(12, 3, 5);
        var (client, sent) = Client(new Plus.HabboHotel.Users.Habbo { Id = 1 });
        var clock = new SnowStormTestSupport.TestClock();
        var manager = new SnowStormManager(store, SnowStormTestSupport.Arenas(), new SnowStormTestSupport.Settings(), new SnowStormTestSupport.Filter(), clock,
            NullLogger<SnowStormManager>.Instance);
        new SnowStormDirectory(store, manager, new SnowStormTestSupport.Settings(), clock, NullLogger<SnowStormDirectory>.Instance).ShowDirectoryStatus(client);
        Assert.Equal(new[] { 2, 0, 0, 0 }, Ints(sent[0].Payload, 4));

        sent.Clear();
        var directory = new SnowStormDirectory(store, manager, new SnowStormTestSupport.Settings(("gamecenter.snowwar.enabled", "1")), clock,
            NullLogger<SnowStormDirectory>.Instance);
        directory.ShowDirectoryStatus(client);
        directory.ShowAccountStatus(client, 0);
        directory.ShowLeaderboard(client, SnowStormLeaderboardKind.WeeklyGroup, 0, 1, -1, 8, 50);
        directory.ShowLeaderboard(client, SnowStormLeaderboardKind.Total, 4, 0, -1, 8, 50);
        directory.PurchaseTokens(client, 1);
        Assert.Equal([ServerPacketHeader.Game2GameDirectoryStatusComposer, ServerPacketHeader.GameAccountStatusComposer,
            ServerPacketHeader.Game2WeeklyGroupLeaderboardComposer, ServerPacketHeader.PurchaseErrorComposer], sent.Select(packet => packet.Header));
        // 10 free games a day, 3 used today, plus 5 bought.
        Assert.Equal(new[] { 0, 0, 12, 12 }, Ints(sent[0].Payload, 4));
        Assert.Equal(new[] { 0, 12, 12 }, Ints(sent[1].Payload, 3));
        Assert.Equal(new SnowStormLeaderboardRequest(SnowStormLeaderboardKind.WeeklyGroup, 1, 1, -1, 8, 50), Assert.Single(store.Requests));
    }

    [Fact]
    public void SkillLevelFollowsThePolarisCurve()
    {
        Assert.Equal((1, 50), (SnowStormSkill.Level(0), SnowStormSkill.ScoreToNextLevel(0)));
        Assert.Equal((2, 150), (SnowStormSkill.Level(50), SnowStormSkill.ScoreToNextLevel(50)));
        Assert.Equal(30, SnowStormSkill.Level(int.MaxValue));
        Assert.Equal(0, SnowStormSkill.ScoreToNextLevel(int.MaxValue));
        Assert.Equal(-1, new SnowStormAccount(0, 0, 4).GamesLeft(-1));
        Assert.Equal(4, new SnowStormAccount(0, 12, 4).GamesLeft(10));
        Assert.Equal(new DateOnly(2026, 10, 5), SnowStormStore.WeekStart(new DateTimeOffset(2026, 10, 11, 23, 59, 0, TimeSpan.Zero)));
        Assert.Equal(60, SnowStormStore.MinutesUntilReset(new DateTimeOffset(2026, 10, 11, 23, 0, 0, TimeSpan.Zero)));
    }

    private static int[] Ints(byte[] payload, int count)
    {
        var packet = SnowStormTestSupport.Read(payload);

        return Enumerable.Range(0, count).Select(_ => packet.ReadInt()).ToArray();
    }

    private static List<object> Write(IServerPacket composer)
    {
        var packet = new RecordingPacket();
        composer.Compose(packet);

        return packet.Writes;
    }

    private sealed class RecordingManager : ISnowStormManager
    {
        public List<string> Calls { get; } = [];
        public void QuickJoin(GameClient session) => Calls.Add("join");
        public void LeaveLobby(GameClient session) => Calls.Add("leave");
        public void LoadStageReady(GameClient session) => Calls.Add("ready");
        public void ExitGame(GameClient session) => Calls.Add("exit");
        public void PlayAgain(GameClient session) => Calls.Add("again");
        public void Chat(GameClient session, string message) => Calls.Add("chat " + message);
        public void SetMoveTarget(GameClient session, int x, int y, int turn, int subturn) => Calls.Add($"move {x} {y} {turn} {subturn}");
        public void ThrowAtPosition(GameClient session, int x, int y, int trajectory, int turn, int subturn) => Calls.Add($"throw {x} {y} {trajectory} {turn} {subturn}");
        public void ThrowAtHuman(GameClient session, int targetHumanId, int trajectory, int turn, int subturn) => Calls.Add($"hit {targetHumanId} {trajectory} {turn} {subturn}");
        public void MakeSnowball(GameClient session, int turn, int subturn) => Calls.Add($"make {turn} {subturn}");
        public void RequestFullStatus(GameClient session, int reason) => Calls.Add($"full {reason}");
        public int BlockSeconds(int userId) => 0;
        public void Tick() { }
    }

    private sealed class RecordingDirectory : ISnowStormDirectory
    {
        public List<string> Calls { get; } = [];
        public void ShowDirectoryStatus(GameClient session) => Calls.Add("directory");
        public void ShowAccountStatus(GameClient session, int gameTypeId) => Calls.Add($"account {gameTypeId}");
        public void ShowLeaderboard(GameClient session, SnowStormLeaderboardKind kind, int gameTypeId, int weekOffset, int startRank, int viewSize, int windowSize) =>
            Calls.Add($"{kind} {gameTypeId} {weekOffset} {startRank} {viewSize} {windowSize}");
        public void ShowTokenOffers(GameClient session) => Calls.Add("offers");
        public void PurchaseTokens(GameClient session, int offerId) => Calls.Add($"buy {offerId}");
    }
}
