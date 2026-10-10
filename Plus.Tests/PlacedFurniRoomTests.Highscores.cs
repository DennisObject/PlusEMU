using System.Collections.Immutable;
using System.Reflection;
using System.Data;
using Plus.HabboHotel.Rooms.PathFinding;
using Dapper;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Commands.User;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.Games;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [WiredChestDatabaseTheory]
    [InlineData(0, 2)]
    [InlineData(17, 5)]
    public void ActualSpeechExplicitFootballStopUpdatesLoadedAllTimeBoards(int score, int seconds)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        typeof(PlacedFurniRoomTests).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, db.Database);
        var clock = new HighscoreClock();
        Set("_interactionClock", clock);
        var map = new Gamemap(_room, new RoomModel("highscore", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false),
            TestLogging.Navigation, new TestRoomSettings(new() { ["pathfinding.engine"] = "v2" }),
            TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        Set("_gamemap", map);
        Plus.HabboHotel.Rooms.Instance.WiredComponent wired = null!;
        RoomUser actor = null!;
        _room.RunFastPass(() => (wired, actor) = PrepareSpeech(clock));
        Assert.True(_room.UsesV2Movement);
        Assert.NotNull(map.Navigation);
        Assert.False(RoomOwnerScope.IsOwner(_room));
        var fastest = HighscoreBoard(501, 3);
        var wins = HighscoreBoard(502, 1);
        var timer = HighscoreTimer();

        foreach (var item in new[] { fastest, wins, timer }) {
            db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,7,42,@id,@payload)", new { item.Id, payload = item.ExtraData.Serialize() });
        }

        var data = new RoomDataComponent(Proxy<IRoomFurnitureLoader>((method, _) => method == "Load" ? new Item[] { fastest, wins, timer } : null));
        data.Initiate(_room);
        data.Initiated();
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(clock);
        Assert.True(WiredGameState.For(_room).Join(_room, actor, 0, Team.Red, 0, [actor]));
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "start" }, 0);
        AddSpeechBox(302, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [timer.Id] }, 1);
        AddSpeechBox(311, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "stop" }, 0, 2, 2);
        AddSpeechBox(312, "wf_act_control_clock", new() { IntParams = [1, 100], SelectedItems = [timer.Id] }, 1, 2, 2);
        AddSpeechBox(303, "wf_xtra_exec_in_order", new(), 2);

        if (score > 0) {
            AddSpeechBox(304, "wf_act_give_score_tm", new() { IntParams = [score, 0, 1] }, 3);
        }

        actor.OnChat(0, "start", false);
        Assert.True(_room.GetSoccer().GameIsStarted);
        Assert.Equal(score, _room.GetGameManager().Points[(int)Team.Red]);
        clock.Now += TimeSpan.FromSeconds(seconds);
        actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Equal(seconds, Assert.Single(Assert.IsType<HighscoreDataFormat>(fastest.ExtraData).Entries).Score);
        var entries = Assert.IsType<HighscoreDataFormat>(wins.ExtraData).Entries;
        Assert.Equal(score > 0 ? 1 : 0, entries.Length);

        if (score > 0) {
            Assert.Equal(1, entries[0].Score);
        }

        actor.OnChat(0, "stop", false);
        Assert.Equal(entries, Assert.IsType<HighscoreDataFormat>(wins.ExtraData).Entries);
    }

    [WiredChestDatabaseFact]
    public void ActualPickAllThenNewRoomPayloadSurvivesOldHandlerDisposal()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        Inventory(new InventoryItem { Id = 999, Definition = HighscoreBoard(999, 1).Definition });
        var handler = new RoomItemHandling(_room, new RoomItemStore(db.Database), new RoomItemMetadataStore(db.Database),
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        Set("_roomItemHandling", handler);
        var board = HighscoreBoard(501, 1);
        var prior = board.ExtraData.Serialize();
        db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(501,7,42,501,@prior)", new { prior });
        handler.LoadFurniture([board]);
        handler.UpdateItem(board);
        new PickAllCommand(db.Database, Proxy<IRoomItemPickupService>((_, _) => throw new InvalidOperationException("no music")))
            .Execute(_client, _room, []);
        Assert.Null(handler.GetItem(board.Id));
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(board.Id));
        var candidate = new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(2, ["owner"])] }.Serialize();
        db.Connection.Execute("UPDATE items SET room_id=43,extra_data=@candidate WHERE id=501", new { candidate });
        _room.GetGameManager().Dispose();
        handler.Dispose();
        Assert.Equal(candidate, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    [WiredChestDatabaseTheory]
    [InlineData("pickall")]
    [InlineData("ejectall")]
    [InlineData("remove-items")]
    public void FailedPreSqlConfigurationResetCanRecoverAndRetryTheReservedSet(string route)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        Inventory(new InventoryItem { Id = 999, Definition = HighscoreBoard(999, 1).Definition });
        var (wired, _) = PrepareSpeech(new HighscoreClock());
        var board = HighscoreBoard(501, 1);
        var box = Furni(601, InteractionType.WiredTrigger, WiredBoxType.TriggerUserSays);
        box.RoomId = RoomId;
        box.UserId = 7;
        box.Definition.Id = box.Id;
        SeedBoards(db, handler, [board, box]);
        var refuse = true;
        var resets = 0;
        typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_configurationStore", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(wired, Proxy<IWiredConfigurationStore>((method, args) =>
            {
                if (method != "Reset") {
                    return null;
                }

                Assert.Contains(box.Id, (IReadOnlyCollection<uint>)args[0]!);
                resets++;

                if (refuse) {
                    throw new IOException("configuration reset refused before transfer SQL");
                }

                return null;
            }));
        var pickup = Proxy<IRoomItemPickupService>((_, _) => throw new InvalidOperationException("no music"));

        if (route == "ejectall") {
            _room.OwnerId = 8;
        }

        void Execute()
        {
            if (route == "pickall") {
                new PickAllCommand(db.Database, pickup).Execute(_client, _room, []);
            }
            else if (route == "ejectall") {
                new EjectAllCommand(TestGameClientManager.Empty, db.Database, pickup).Execute(_client, _room, []);
            }
            else {
                Assert.Equal(2, handler.RemoveItems(_client).Count);
            }
        }
        Assert.Throws<IOException>(Execute);
        Assert.Equal(1, resets);
        Assert.Same(board, handler.GetItem(board.Id));
        Assert.Same(box, handler.GetItem(box.Id));
        Assert.Equal(new uint[] { RoomId, RoomId }, db.Connection.Query<uint>("SELECT room_id FROM items WHERE id IN(501,601) ORDER BY id").ToArray());
        refuse = false;
        Execute();
        Assert.True(resets >= 2);
        Assert.Null(handler.GetItem(board.Id));
        Assert.Null(handler.GetItem(box.Id));

        if (route != "remove-items") {
            Assert.All(db.Connection.Query<uint>("SELECT room_id FROM items WHERE id IN(501,601)"), id => Assert.Equal(0u, id));
        }
    }

    [WiredChestDatabaseFact]
    public void ActualCommittedCandidatesWithUnavailableReconciliationSurviveOrderedDisposalAndReload()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        var handler = new RoomItemHandling(_room, new RoomItemStore(db.Database), new RoomItemMetadataStore(db.Database),
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        Set("_roomItemHandling", handler);
        var fastest = HighscoreBoard(501, 3);
        var wins = HighscoreBoard(502, 1);
        handler.LoadFurniture([fastest, wins]);
        var prior = new[] { fastest.ExtraData.Serialize(), wins.ExtraData.Serialize() };
        var candidate = new[]
        {
            new HighscoreDataFormat { State = "0", ScoreType = 3, Entries = [new(2, ["owner"])] }.Serialize(),
            new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(1, ["owner"])] }.Serialize()
        };

        for (var i = 0; i < 2; i++) {
            var item = i == 0 ? fastest : wins;
            db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,7,42,@id,@payload)",
                new { item.Id, payload = prior[i] });
            handler.UpdateItem(item);
        }

        // The real batch store commits both rows, then loses acknowledgement and the fresh read.
        var writes = new[] { fastest, wins }.Select((item, i) =>
            new HighscoreWrite(item, item.Placement, item.Definition, RoomId, item.OwnerId, prior[i], candidate[i])).ToArray();
        Assert.True(handler.TryRegisterHighscores(writes, out var batch));
        var uncertain = new UnavailableAfterCommitStore(new RoomHighscoreStore(db.Database));
        Assert.Equal(HighscoreResolution.Unknown, handler.ResolveHighscores(batch!, uncertain, true));
        Assert.True(uncertain.Committed);
        Assert.Equal(prior[0], fastest.ExtraData.Serialize());
        Assert.Equal(prior[1], wins.ExtraData.Serialize());
        Assert.Equal(candidate, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id"));

        _room.GetGameManager().Dispose();
        handler.Dispose();

        var saved = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
        Assert.Equal(candidate, saved);

        for (var i = 0; i < saved.Length; i++) {
            var loaded = new HighscoreDataFormat();
            loaded.Store(saved[i]);
            Assert.Equal(candidate[i], loaded.Serialize());
        }
    }

    [WiredChestDatabaseFact]
    public void ActualSpeechAndDirectSpeechDispatchOwnAdmissionsBeforeClockCallbacks()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var observations = new List<(bool Owner, bool RoomLock)>();
        _client.BeforeCapture = _ => observations.Add((RoomOwnerScope.IsOwner(_room), Monitor.IsEntered(_room.NavigationSync)));
        _room.GetGameManager().Points[1] = 99;
        game.Actor.OnChat(0, "start", false);
        Assert.Equal(17, _room.GetGameManager().Points[1]); // Reset precedes the new round and subsequent grant.
        game.Clock.Now += TimeSpan.FromSeconds(5);
        Assert.False(game.Wired.Dispatch(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Actor = game.Actor, Message = "stop" }));
        Assert.Contains((true, true), observations);
        Assert.False(RoomOwnerScope.IsOwner(_room));
        Assert.False(Monitor.IsEntered(_room.NavigationSync));
        Assert.Equal(5, Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries).Score);
    }

    [WiredChestDatabaseFact]
    public void SlowerRepeatRetainsFastestAndAddsExactlyOneWinPerExplicitRound()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);

        foreach (var seconds in new[] { 2, 3, 6 }) {
            game.Actor.OnChat(0, "start", false);
            game.Clock.Now += TimeSpan.FromSeconds(seconds);
            _room.RunFastPass(game.Wired.OnFastCycle); // Real counter poll/drain between start and stop.
            game.Actor.OnChat(0, "stop", false);
            game.Actor.OnChat(0, "stop", false);
        }

        Assert.Equal(2, Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries).Score);
        Assert.Equal(3, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        Assert.Equal(game.Wins.ExtraData.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=502"));
    }

    [WiredChestDatabaseFact]
    public void AllPriorReconciliationRetriesOnlyTheFrozenBatchOnALaterOwnedPass()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new FirstAttemptRejectedStore(new RoomHighscoreStore(db.Database));
        var game = PrepareHighscoreGame(db, store);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(1, store.Commits);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        _room.RunFastPass(game.Wired.OnFastCycle);
        Assert.Equal(2, store.Commits);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        _room.RunFastPass(game.Wired.OnFastCycle);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(2, store.Commits);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
    }

    [WiredChestDatabaseTheory]
    [InlineData("gui")]
    [InlineData("pause")]
    [InlineData("reset")]
    [InlineData("game-start")]
    [InlineData("expiry")]
    [InlineData("actor-lifetime")]
    [InlineData("clock-placement")]
    [InlineData("clock-owner")]
    [InlineData("clock-base")]
    [InlineData("nested-off-owner")]
    public void UnsupportedTransitionOrSourceMutationDoesNotAccrue(string change)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);

        switch (change) {
            case "gui":
                game.Wired.TryUseCounter(game.Timer, 0);
                break;
            case "pause":
            case "reset":
                AddSpeechBox(320, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "unsupported" }, 0, 3, 3);
                AddSpeechBox(321, "wf_act_control_clock", new() { IntParams = [change == "pause" ? 3 : 2, 100], SelectedItems = [503] }, 1, 3, 3);
                game.Actor.OnChat(0, "unsupported", false);
                break;
            case "game-start":
                game.Wired.TriggerEvent(WiredBoxType.TriggerGameStarts);
                break;
            case "expiry":
                game.Clock.Now += TimeSpan.FromSeconds(60);

                for (var i = 0; i < 31; i++) {
                    game.Clock.Now += TimeSpan.FromSeconds(1);
                    _room.RunFastPass(game.Wired.OnFastCycle);
                }

                break;
            case "actor-lifetime":
                game.Actor.Movement.State = Plus.HabboHotel.Rooms.PathFinding.NavState.Removing;
                break;
            case "clock-placement":
                game.Timer.GetX = 2;
                break;
            case "clock-owner":
                game.Timer.OwnerId = 8;
                break;
            case "clock-base":
                game.Timer.Definition.Id = 999;
                break;
            case "nested-off-owner":
                var engine = (WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game.Wired)!;
                engine.Mutate(() => game.Wired.Dispatch(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Actor = game.Actor, Message = "stop" }));
                break;
        }

        game.Actor.OnChat(0, "stop", false);
        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
    }

    [WiredChestDatabaseFact]
    public void DeniedPickupCannotStrandAnUnstartedReservation()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        Inventory(new InventoryItem { Id = 999, Definition = HighscoreBoard(999, 1).Definition });
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        var service = new RoomItemPickupService(TestGameClientManager.Empty, Proxy<Plus.HabboHotel.Quests.IQuestManager>((_, _) => null), new RoomItemPickupStore(db.Database));
        var closed = typeof(Plus.HabboHotel.Users.Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!;
        closed.SetValue(_client.GetHabbo(), true);
        Assert.False(service.TryPickUp(_client, board.Id));
        closed.SetValue(_client.GetHabbo(), false);
        Assert.True(service.TryPickUp(_client, board.Id));
        Assert.Null(handler.GetItem(board.Id));
        Assert.NotNull(_client.GetHabbo().Inventory.Furniture.GetItem(board.Id));
    }

    [WiredChestDatabaseFact]
    public void UncertainTransferCannotBeReleasedByALaterDenialOrRetry()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        Inventory(new InventoryItem { Id = 999, Definition = HighscoreBoard(999, 1).Definition });
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        var real = new RoomItemPickupStore(db.Database);
        var fault = Proxy<IRoomItemPickupStore>((method, args) => { Assert.True(real.PickUp((RoomItemPickup)args[0]!)); throw new IOException("pickup commit acknowledgement lost"); });
        var service = new RoomItemPickupService(TestGameClientManager.Empty, Proxy<Plus.HabboHotel.Quests.IQuestManager>((_, _) => null), fault);
        Assert.Throws<IOException>(() => service.TryPickUp(_client, board.Id));
        Assert.False(service.TryPickUp(_client, board.Id));
        Assert.False(handler.PersistFurnitureData(board, new HighscoreDataFormat { ScoreType = 1, State = "0" }));
        handler.UpdateItem(board);
        _room.GetGameManager().Dispose();
        handler.Dispose();
        Assert.Equal(0u, db.Connection.QuerySingle<uint>("SELECT room_id FROM items WHERE id=501"));
        Assert.Equal(board.ExtraData.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
        Assert.Null(_client.GetHabbo().Inventory.Furniture.GetItem(board.Id));
    }

    private (HighscoreClock Clock, RoomUser Actor, Plus.HabboHotel.Rooms.Instance.WiredComponent Wired, Item Fastest, Item Wins, Item Timer)
        PrepareHighscoreGame(WiredChestDatabaseTests.Fixture db, IRoomHighscoreStore? store = null)
    {
        typeof(PlacedFurniRoomTests).GetField("_database", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, db.Database);
        var clock = new HighscoreClock();
        Set("_interactionClock", clock);
        Set("_gamemap", new Gamemap(_room, new RoomModel("highscore", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false),
            TestLogging.Navigation, new TestRoomSettings(new() { ["pathfinding.engine"] = "v2" }),
            TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        Plus.HabboHotel.Rooms.Instance.WiredComponent wired = null!;
        RoomUser actor = null!;
        _room.RunFastPass(() => (wired, actor) = PrepareSpeech(clock));

        if (store != null) {
            wired.InitializeHighscores(store);
        }

        var fastest = HighscoreBoard(501, 3);
        var wins = HighscoreBoard(502, 1);
        var timer = HighscoreTimer();

        foreach (var item in new[] { fastest, wins, timer }) {
            db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,7,42,@id,@payload)", new { item.Id, payload = item.ExtraData.Serialize() });
        }

        var data = new RoomDataComponent(Proxy<IRoomFurnitureLoader>((method, _) => method == "Load" ? new Item[] { fastest, wins, timer } : null));
        data.Initiate(_room);
        data.Initiated();
        _client.GetHabbo().Effects = new Plus.HabboHotel.Users.Effects.EffectsComponent(clock);
        Assert.True(WiredGameState.For(_room).Join(_room, actor, 0, Team.Red, 0, [actor]));
        AddSpeechBox(301, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "start" }, 0);
        AddSpeechBox(302, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [timer.Id] }, 1);
        AddSpeechBox(303, "wf_xtra_exec_in_order", new(), 2);
        AddSpeechBox(304, "wf_act_give_score_tm", new() { IntParams = [17, 0, 1] }, 3);
        AddSpeechBox(311, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "stop" }, 0, 2, 2);
        AddSpeechBox(312, "wf_act_control_clock", new() { IntParams = [1, 100], SelectedItems = [timer.Id] }, 1, 2, 2);

        return (clock, actor, wired, fastest, wins, timer);
    }

    private RoomItemHandling DurableHandler(WiredChestDatabaseTests.Fixture db)
    {
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        var handler = new RoomItemHandling(_room, new RoomItemStore(db.Database), new RoomItemMetadataStore(db.Database),
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        Set("_roomItemHandling", handler);

        return handler;
    }

    private static void SeedBoards(WiredChestDatabaseTests.Fixture db, RoomItemHandling handler, Item[] boards)
    {
        foreach (var board in boards) {
            db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(@id,@OwnerId,@RoomId,@baseItem,@payload)",
                new { board.Id, board.OwnerId, board.RoomId, baseItem = board.Definition.Id, payload = board.ExtraData.Serialize() });
        }

        handler.LoadFurniture(boards);
    }

    private sealed class FirstAttemptRejectedStore(IRoomHighscoreStore inner) : IRoomHighscoreStore
    {
        public int Commits;
        public bool Commit(IReadOnlyList<HighscoreWrite> writes) => ++Commits > 1 && inner.Commit(writes);
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes) => inner.Read(writes);
    }

    [WiredChestDatabaseFact]
    public void PreparedHighscorePersistCallbackRunsOutsideGateAndCannotEnrollAccrual()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        var data = new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(2, ["owner"])] };
        var gate = typeof(RoomItemHandling).GetField("_payloadGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        var held = false;
        var registered = false;
        var write = new HighscoreWrite(board, board.Placement, board.Definition, RoomId, board.OwnerId, board.ExtraData.Serialize(), data.Serialize());
        Assert.True(handler.SetFloorItemData(_client, board, data, () =>
        {
            held = Monitor.IsEntered(gate);
            registered = handler.TryRegisterHighscores([write], out _);
            _room.GetWired().TriggerEvent(WiredBoxType.TriggerGameStarts);
            db.Connection.Execute("UPDATE items SET extra_data=@payload WHERE id=501", new { payload = data.Serialize() });
        }));
        Assert.False(held);
        Assert.False(registered);
        Assert.Equal(data.Serialize(), board.ExtraData.Serialize());
        Assert.True(handler.TryReserveTransfers([board], out var transfer));
        handler.CancelUnstartedTransfer(transfer!);
    }

    [WiredChestDatabaseFact]
    public void UncertainPreparedPersistSurvivesOldPayloadSaveAndDisposal()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        handler.UpdateItem(board);
        var prior = board.ExtraData.Serialize();
        var data = new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(2, ["owner"])] };
        Assert.Throws<IOException>(() => handler.SetFloorItemData(_client, board, data, () =>
        {
            db.Connection.Execute("UPDATE items SET extra_data=@payload WHERE id=501", new { payload = data.Serialize() });
            throw new IOException("prepared payload acknowledgement lost AFTER commit");
        }));
        Assert.Equal(prior, board.ExtraData.Serialize());
        Assert.False(handler.TryReserveTransfers([board], out _));
        _room.GetGameManager().Dispose();
        handler.Dispose();
        Assert.Equal(data.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    [WiredChestDatabaseFact]
    public void CompletedCandidateReconcilesAfterActorDepartureWithoutAnotherIncrement()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new RecoveringAcknowledgementStore(new RoomHighscoreStore(db.Database));
        var game = PrepareHighscoreGame(db, store);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(1, store.Commits);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        game.Actor.Movement.State = NavState.Removing;
        store.Available = true;
        _room.RunFastPass(game.Wired.OnFastCycle);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        Assert.Equal(1, store.Commits);
        Assert.True(_room.GetRoomItemHandler().TryReserveTransfers([game.Wins], out var transfer));
        _room.GetRoomItemHandler().CancelUnstartedTransfer(transfer!);
        _room.RunFastPass(game.Wired.OnFastCycle);
        Assert.Equal(1, store.Commits);
    }

    [WiredChestDatabaseFact]
    public void MixedDurableRowsStayQuarantinedWithoutInstallingOrRecomputing()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var boards = new[] { HighscoreBoard(501, 3), HighscoreBoard(502, 1) };
        SeedBoards(db, handler, boards);
        var writes = CandidateWrites(boards);
        Assert.True(handler.TryRegisterHighscores(writes, out var batch));
        db.Connection.Execute("UPDATE items SET extra_data=@Candidate WHERE id=501", writes[0]);
        Assert.Equal(HighscoreResolution.Unknown, handler.ResolveHighscores(batch!, new RoomHighscoreStore(db.Database), false));
        Assert.All(boards, board => Assert.Empty(((HighscoreDataFormat)board.ExtraData).Entries));
        Assert.False(handler.TryReserveTransfers(boards, out _));
        Assert.False(handler.PersistFurnitureData(boards[1], new HighscoreDataFormat { State = "0", ScoreType = 1 }));

        foreach (var board in boards) {
            handler.UpdateItem(board);
        }

        handler.Dispose();
        Assert.Equal(writes[0].Candidate, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
        Assert.Equal(writes[1].Prior, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=502"));
    }

    [WiredChestDatabaseFact]
    public void ReservationAndAccrualRegistrationExcludeEachOtherBeforeSerialization()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        var writes = CandidateWrites([board]);
        Assert.True(handler.TryReserveTransfers([board], out var transfer));
        Assert.False(handler.TryRegisterHighscores(writes, out _));
        handler.CancelUnstartedTransfer(transfer!);
        Assert.True(handler.TryRegisterHighscores(writes, out var batch));
        Assert.False(handler.TryReserveTransfers([board], out _));
        Assert.False(handler.RemoveFurniture(_client, board));
        Assert.False(handler.SetFloorItemData(_client, board, new HighscoreDataFormat(), () => throw new InvalidOperationException("must not invoke blocked writer")));
        Assert.Equal(HighscoreResolution.Candidate, handler.ResolveHighscores(batch!, new RoomHighscoreStore(db.Database), true));
        Assert.True(handler.TryReserveTransfers([board], out var recovered));
        handler.CancelUnstartedTransfer(recovered!);
    }

    [WiredChestDatabaseFact]
    public async Task SaveMovedEmissionHoldsThePayloadGateAcrossAlreadyBuiltDtos()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        var blocking = new BlockingSaveStore(new RoomItemStore(db.Database));
        var handler = new RoomItemHandling(_room, blocking, new RoomItemMetadataStore(db.Database), TestGameClientManager.Empty,
            TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        Set("_roomItemHandling", handler);
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        handler.UpdateItem(board);
        var saving = Task.Run(() => InvokeFurnitureSave(handler));
        await blocking.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(board.ExtraData.Serialize(), Assert.Single(blocking.Items).ExtraData);
        var gate = typeof(RoomItemHandling).GetField("_payloadGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        var entered = Monitor.TryEnter(gate);

        if (entered) {
            Monitor.Exit(gate);
        }

        Assert.False(entered); // DTO capture has happened, but the SQL emission still owns the same gate.
        var registering = Task.Run(() => handler.TryRegisterHighscores(CandidateWrites([board]), out var batch) ? batch : null);
        blocking.Release.Set();
        await saving.WaitAsync(TimeSpan.FromSeconds(5));
        var batch = await registering.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(batch);
        Assert.Equal(HighscoreResolution.Candidate, handler.ResolveHighscores(batch!, new RoomHighscoreStore(db.Database), true));
        Assert.Equal(batch!.Writes[0].Candidate, db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    [WiredChestDatabaseFact]
    public async Task ActualStopAndConcurrentSaveUseConsistentItemLockOrder()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        var game = PrepareHighscoreGame(db);
        var handler = _room.GetRoomItemHandler();
        typeof(RoomItemHandling).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handler, new RoomItemStore(db.Database));
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        handler.UpdateItem(game.Fastest);
        handler.UpdateItem(game.Wins);
        using var go = new ManualResetEventSlim();
        var save = Task.Run(() => { go.Wait(); InvokeFurnitureSave(handler); });
        var stop = Task.Run(() => { go.Wait(); game.Actor.OnChat(0, "stop", false); });
        go.Set();
        await Task.WhenAll(save, stop).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        Assert.Equal(game.Wins.ExtraData.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=502"));
    }

    [WiredChestDatabaseTheory]
    [InlineData("placement")]
    [InlineData("owner")]
    [InlineData("base")]
    [InlineData("row-room")]
    public void PendingCandidateCannotInstallThroughChangedBoardIdentity(string change)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var board = HighscoreBoard(501, 1);
        SeedBoards(db, handler, [board]);
        var writes = CandidateWrites([board]);
        Assert.True(handler.TryRegisterHighscores(writes, out var batch));
        Assert.True(new RoomHighscoreStore(db.Database).Commit(writes));

        switch (change) {
            case "placement":
                board.Attach(_room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
                break;
            case "owner":
                board.OwnerId = 8;
                break;
            case "base":
                board.Definition.Id = 999;
                break;
            case "row-room":
                db.Connection.Execute("UPDATE items SET room_id=43 WHERE id=501");
                break;
        }

        Assert.Equal(HighscoreResolution.Unknown, handler.ResolveHighscores(batch!, new RoomHighscoreStore(db.Database), false));
        Assert.Empty(((HighscoreDataFormat)board.ExtraData).Entries);
        Assert.False(handler.TryReserveTransfers([board], out _));
    }

    [WiredChestDatabaseFact]
    public void BulkCaptureCannotPickUpAnItemPlacedAfterReservation()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var first = HighscoreBoard(501, 1);
        var later = HighscoreBoard(502, 3);
        SeedBoards(db, handler, [first]);
        Assert.True(handler.TryReserveTransfers([first], out var transfer));
        db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(502,7,42,502,@payload)", new { payload = later.ExtraData.Serialize() });
        Assert.True(handler.SetFloorItem(null!, later, 2, 2, 0, true, false, false));
        Assert.True(handler.BeginTransferSql(transfer!, [first]));
        var entry = Assert.Single(transfer!.Entries);
        Assert.True(new RoomItemPickupStore(db.Database).PickUpMany([new(entry.ItemId, entry.RoomId, (int)entry.OwnerId, 7, InteractionType.None, false, entry.BaseItem)]));
        Assert.Equal(0u, db.Connection.QuerySingle<uint>("SELECT room_id FROM items WHERE id=501"));
        Assert.Equal(42u, db.Connection.QuerySingle<uint>("SELECT room_id FROM items WHERE id=502"));
        Assert.Same(later, handler.GetItem(502));
    }

    private static HighscoreWrite[] CandidateWrites(Item[] boards) => boards.Select(board =>
        new HighscoreWrite(board, board.Placement, board.Definition, board.RoomId, board.OwnerId, board.ExtraData.Serialize(),
            new HighscoreDataFormat
            {
                State = "0",
                ScoreType = ((HighscoreDataFormat)board.ExtraData).ScoreType,
                Entries = [new(((HighscoreDataFormat)board.ExtraData).ScoreType == 3 ? 2 : 1, ["owner"])]
            }.Serialize())).ToArray();

    private static void InvokeFurnitureSave(RoomItemHandling handler) =>
        typeof(RoomItemHandling).GetMethod("SaveFurniture", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(handler, null);

    private sealed class RecoveringAcknowledgementStore(IRoomHighscoreStore inner) : IRoomHighscoreStore
    {
        public int Commits;
        public bool Available;
        public bool Commit(IReadOnlyList<HighscoreWrite> writes)
        {
            Commits++;
            Assert.True(inner.Commit(writes));
            throw new IOException("acknowledgement lost AFTER real COMMIT");
        }
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes) => Available || Commits == 0 ? inner.Read(writes) : throw new IOException("temporarily unavailable read");
    }

    private sealed class BlockingSaveStore(IRoomItemStore inner) : IRoomItemStore
    {
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public IReadOnlyList<RoomItemSave> Items = [];
        public void SaveMoved(IReadOnlyList<RoomItemSave> items)
        {
            Items = items;
            Captured.TrySetResult();

            if (!Release.Wait(TimeSpan.FromSeconds(5))) {
                throw new TimeoutException("bounded save release");
            }

            inner.SaveMoved(items);
        }
        public void AssignOwner(uint id, int owner) => inner.AssignOwner(id, owner);
        public void ClearRoom(uint id) => inner.ClearRoom(id);
        public void SaveWallPosition(uint id, string wall) => inner.SaveWallPosition(id, wall);
        public void PlaceFloor(uint id, uint room, int x, int y, double z, int rot) => inner.PlaceFloor(id, room, x, y, z, rot);
        public void PlaceWall(uint id, uint room, int x, int y, double z, int rot, string wall) => inner.PlaceWall(id, room, x, y, z, rot, wall);
    }

    [WiredChestDatabaseFact]
    public void ActualLandingTeamDepartureAndFootballRejoinDoesNotAccrue()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var gate = Add(700, 1, 1, type: InteractionType.Banzaigatered);
        gate.Team = Team.Red;
        InitializeNativeState(gate);
        game.Actor.OnChat(0, "start", false);
        game.Actor.X = 1;
        game.Actor.Y = 1;
        game.Actor.GoalX = 1;
        game.Actor.GoalY = 1;
        game.Actor.Z = gate.GetZ;
        _client.GetHabbo().Client = _client;
        _client.GetHabbo().Gender = "M";
        _client.GetHabbo().Effects.Init(_client.GetHabbo());
        _room.RunFastPass(() => new LandingEffects(_room, db.Database).Apply(game.Actor, false));
        Assert.Equal(Team.None, game.Actor.Team);
        Assert.True(WiredGameState.For(_room).Join(_room, game.Actor, 0, Team.Red, 0, [game.Actor]));
        Assert.Equal(4, WiredGameState.For(_room).ReadTeamType(_room, game.Actor));
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
    }

    [WiredChestDatabaseFact]
    public void SameTeamAssignmentPreservesASupportedRound()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        game.Actor.OnChat(0, "start", false);
        game.Actor.Team = Team.Red;
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
    }

    [WiredChestDatabaseFact]
    public async Task ActualPlacementAToBToAAndLateOldReferenceCannotOverwriteTypedHistory()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handlerA = DurableHandler(db);
        Inventory(new InventoryItem { Id = 999, Definition = HighscoreBoard(999, 1).Definition });
        var oldA = HighscoreBoard(501, 1);
        SeedBoards(db, handlerA, [oldA]);
        handlerA.UpdateItem(oldA);
        var pickup = new PickAllCommand(db.Database, Proxy<IRoomItemPickupService>((_, _) => throw new InvalidOperationException("no music")));
        pickup.Execute(_client, _room, []);
        Assert.Null(handlerA.GetItem(501));
        var roomB = HighscoreOtherRoom(db, 43);
        _client.GetHabbo().CurrentRoom = roomB;
        await PlaceObject().Parse(roomB, _client, ClientPacket("501 1 1 0"));
        var placedB = Assert.IsType<Item>(roomB.GetRoomItemHandler().GetItem(501));
        Assert.NotSame(oldA, placedB);
        var dataB = new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(2, ["owner"])] };
        Assert.True(roomB.GetRoomItemHandler().PersistFurnitureData(placedB, dataB));
        pickup.Execute(_client, roomB, []);
        Assert.Null(roomB.GetRoomItemHandler().GetItem(501));
        // A callback from the original, detached placement arrives before the new A placement.
        handlerA.UpdateItem(oldA);
        _client.GetHabbo().CurrentRoom = _room;
        await PlaceObject().Parse(_room, _client, ClientPacket("501 1 1 0"));
        var newA = Assert.IsType<Item>(handlerA.GetItem(501));
        Assert.NotSame(oldA, newA);
        Assert.Equal(dataB.Serialize(), newA.ExtraData.Serialize());
        var dataA = new HighscoreDataFormat { State = "0", ScoreType = 1, Entries = [new(3, ["owner"])] };
        Assert.True(handlerA.PersistFurnitureData(newA, dataA));
        _room.GetGameManager().Dispose();
        handlerA.Dispose();
        roomB.GetGameManager().Dispose();
        roomB.GetRoomItemHandler().Dispose();
        Assert.Equal(dataA.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void EjectHonorsPendingRefusalBeforeSqlOrOnlineOfflineInventoryExport(bool online)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var handler = DurableHandler(db);
        var board = HighscoreBoard(501, 1);
        board.OwnerId = 8;
        board.UserId = 8;
        SeedBoards(db, handler, [board]);
        Assert.True(handler.TryRegisterHighscores(CandidateWrites([board]), out _));
        var receiver = new TestClient();
        receiver.SetHabbo(new Plus.HabboHotel.Users.Habbo { Id = 8, Username = "recipient" });
        receiver.GetHabbo().Inventory = new Plus.HabboHotel.Users.Inventory.InventoryComponent
        {
            Furniture = new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryComponent([], [])
        };
        var clients = Proxy<Plus.HabboHotel.GameClients.IGameClientManager>((_, _) => online ? receiver : null);
        var pickup = Proxy<IRoomItemPickupService>((_, _) => throw new InvalidOperationException("no pickup may begin after bulk refusal"));
        new EjectAllCommand(clients, db.Database, pickup).Execute(_client, _room, []);
        Assert.Same(board, handler.GetItem(501));
        Assert.Equal(42u, db.Connection.QuerySingle<uint>("SELECT room_id FROM items WHERE id=501"));
        Assert.Null(receiver.GetHabbo().Inventory.Furniture.GetItem(501));
        Assert.Empty(receiver.Sent);
    }

    [WiredChestDatabaseFact]
    public void UnsupportedGameStartWaitingOnAnotherThreadsClockAdmissionInvalidatesTheRound()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        Add(710, 3, 3, type: InteractionType.Banzaiscorered);
        _room.GetGameManager().Points[1] = 99;
        var admission = typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_highscoreClockAdmission", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Thread? outsider = null;
        Exception? failure = null;
        var sawTypedScope = false;
        var waitedForEngine = false;
        _client.BeforeCapture = header =>
        {
            if (header != Plus.Communication.Packets.Outgoing.ServerPacketHeader.ObjectUpdateComposer || outsider != null) {
                return;
            }

            sawTypedScope = (int)admission.GetValue(game.Wired)! == 1 && RoomOwnerScope.IsOwner(_room);
            outsider = new Thread(() =>
            {
                try {
                    game.Wired.TriggerEvent(WiredBoxType.TriggerGameStarts);
                }
                catch (Exception error) {
                    failure = error;
                }
            });
            outsider.Start();
            waitedForEngine = SpinWait.SpinUntil(() => (outsider.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));
        };
        game.Actor.OnChat(0, "start", false);
        Assert.NotNull(outsider);
        Assert.True(outsider!.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(failure);
        Assert.True(sawTypedScope);
        Assert.True(waitedForEngine);
        Assert.Equal(0, (int)admission.GetValue(game.Wired)!);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
    }

    [WiredChestDatabaseFact]
    public void ThrowingActualBoardPacketCallbackCannotRerunADurableCompletion()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new CountingHighscoreStore(new RoomHighscoreStore(db.Database));
        var game = PrepareHighscoreGame(db, store);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        var failedBoards = new List<uint>();
        var objectUpdates = new List<uint>();
        var installedTogether = true;
        _client.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.Span;

            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]) !=
                Plus.Communication.Packets.Outgoing.ServerPacketHeader.ObjectUpdateComposer) {
                return false;
            }

            var id = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[6..]);
            objectUpdates.Add(id);

            if (id is not (501 or 502)) {
                return false;
            }

            failedBoards.Add(id);
            installedTogether &= ((HighscoreDataFormat)game.Fastest.ExtraData).Entries.SingleOrDefault()?.Score == 2
                && ((HighscoreDataFormat)game.Wins.ExtraData).Entries.SingleOrDefault()?.Score == 1;
            throw new IOException("actual GameClient packet callback failure after paired commit");
        };
        game.Actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Equal(new uint[] { 501, 502 }, failedBoards); // Room.SendPacket logs and swallows each failed board broadcast.
        Console.WriteLine("Actual ObjectUpdate IDs: " + string.Join(",", objectUpdates));
        Assert.Equal(new uint[] { 311, 312 }, objectUpdates.Except(failedBoards).Order()); // Existing stop-stack flashes are not board notifications.
        Assert.True(installedTogether);
        Assert.Equal(1, store.Commits);
        _client.SendCallback = _ => false;
        game.Actor.OnChat(0, "stop", false);
        _room.RunFastPass(game.Wired.OnFastCycle);
        Assert.Equal(1, store.Commits);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        Assert.Equal(new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() },
            db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id"));
    }

    [WiredChestDatabaseTheory]
    [InlineData("many-rows")]
    [InlineData("different-name")]
    [InlineData("period")]
    public void UnsupportedPriorHistorySurvivesActualRoundSaveAndOrderedDisposal(string history)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        db.Connection.Execute("ALTER TABLE items ADD x INT DEFAULT 0,ADD y INT DEFAULT 0,ADD z DOUBLE DEFAULT 0,ADD rot INT DEFAULT 0,ADD wall_pos TEXT");
        var game = PrepareHighscoreGame(db);
        var handler = _room.GetRoomItemHandler();
        typeof(RoomItemHandling).GetField("_store", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(handler, new RoomItemStore(db.Database));

        foreach (var board in new[] { game.Fastest, game.Wins }) {
            var data = (HighscoreDataFormat)board.ExtraData;
            data.Entries = history == "many-rows"
                ? Enumerable.Range(0, 97).Select(i => new HighscoreEntry(100 + i, ["historical-" + i, "teammate-" + i])).ToImmutableArray()
                : [new(10, [history == "different-name" ? "another-owner" : "owner"])];

            if (history == "period") {
                board.Definition.ItemName = board.Definition.ItemName.Replace("*1", "*2");
                data.ClearType = 1;
            }

            db.Connection.Execute("UPDATE items SET extra_data=@payload WHERE id=@ItemId", new { payload = data.Serialize(), ItemId = board.Id });
            handler.UpdateItem(board);
        }

        var expected = new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() };
        game.Actor.OnChat(0, "start", false);
        Assert.True(_room.GetSoccer().GameIsStarted);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Equal(expected, new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() });
        InvokeFurnitureSave(handler);

        foreach (var board in new[] { game.Fastest, game.Wins }) {
            handler.UpdateItem(board);
        }

        _room.GetGameManager().Dispose();
        handler.Dispose();
        var saved = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
        Assert.Equal(expected, saved);

        foreach (var payload in saved) {
            var reloaded = new HighscoreDataFormat();
            reloaded.Store(payload);
            Assert.Equal(payload, reloaded.Serialize());

            if (history == "many-rows") {
                Assert.Equal(97, reloaded.Entries.Length);
            }
        }
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualBoardCallbackStopSeesStoppedClockAndOnlyOneGameEndEdge(bool directController)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new CountingHighscoreStore(new RoomHighscoreStore(db.Database));
        var game = PrepareHighscoreGame(db, store);
        var controller = HighscoreController(game.Wired);
        var engine = (Plus.HabboHotel.Items.Wired.WiredStackEngine)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game.Wired)!;
        var observe = engine.ObserveEvent;
        var ends = 0;
        engine.ObserveEvent = (evt, now) =>
        {
            if (evt.Kind == Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.GameEnd) {
                ends++;
            }

            observe?.Invoke(evt, now);
        };
        AddSpeechBox(730, "wf_trg_game_ends", new(), 0, 3, 3);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        var reentered = false;
        var wasStopped = false;
        _client.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.Span;

            if (!reentered && System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes[4..]) ==
                Plus.Communication.Packets.Outgoing.ServerPacketHeader.ObjectUpdateComposer &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes[6..]) == 501) {
                reentered = true;
                wasStopped = !controller.IsRunning(game.Timer);

                if (directController) {
                    Assert.True(RoomOwnerScope.IsOwner(_room));
                    controller.Control(game.Timer, 1, game.Clock.Now.ToUnixTimeMilliseconds(),
                        Plus.HabboHotel.Items.Wired.Modern.Actions.WiredClockOrigin.ModernWired, game.Actor);
                }
                else {
                    game.Actor.OnChat(0, "stop", false);
                }
            }

            return false;
        };
        game.Actor.OnChat(0, "stop", false);

        for (var i = 0; i < 3; i++) {
            _room.RunFastPass(game.Wired.OnCycle);
        }

        Assert.True(reentered);
        Console.WriteLine($"Observed board callback stopped={wasStopped}, actual GameEnds={ends}, durable commits={store.Commits}");
        Assert.True(wasStopped);
        Assert.Equal(1, ends);
        Assert.Equal(1, store.Commits);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
    }

    [WiredChestDatabaseFact]
    public void ActualSecondaryFootballClockStartsCannotReplaceAnActiveSource()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var other = AddSecondHighscoreClock(db, game.Wired);
        var controller = HighscoreController(game.Wired);

        for (var i = 0; i < 2; i++) {
            game.Actor.OnChat(0, "start", false);
            Assert.True(controller.IsRunning(game.Timer));
            game.Actor.OnChat(0, "start-b", false);
            Assert.True(controller.IsRunning(game.Timer));
            Assert.True(controller.IsRunning(other));
            _room.GetGameManager().Points[1] = 17;
            game.Clock.Now += TimeSpan.FromSeconds(2);
            game.Actor.OnChat(0, "stop-b", false);
            Assert.False(controller.IsRunning(other));
            Assert.False(controller.IsRunning(game.Timer)); // Existing Soccer GameEnds stops every game-aware clock.
        }

        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
    }

    [WiredChestDatabaseFact]
    public void RealGameEndsRestartedSourceStillExcludesSecondaryClockWithoutARound()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var other = AddSecondHighscoreClock(db, game.Wired);
        var controller = HighscoreController(game.Wired);
        AddSpeechBox(730, "wf_trg_game_ends", new(), 0, 3, 3);
        AddSpeechBox(731, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [503] }, 1, 3, 3);
        game.Actor.OnChat(0, "start", false);
        game.Actor.OnChat(0, "start-b", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop-b", false);

        for (var i = 0; i < 3; i++) {
            _room.RunFastPass(game.Wired.OnCycle);
        }

        Assert.True(controller.IsRunning(game.Timer)); // Actual GameEnds action restarted A after the global stop.
        Assert.False(controller.IsRunning(other));
        Assert.Null(typeof(RoomHighscores).GetField("_round", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(_room.GetGameManager().Highscores)); // GameEnds has no actor: normal clock runs, no producer round.
        var prior = new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() };
        game.Actor.OnChat(0, "start-b", false);
        Assert.True(controller.IsRunning(game.Timer));
        Assert.True(controller.IsRunning(other));
        _room.GetGameManager().Points[1] = 17;
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop-b", false);
        Assert.Equal(prior, new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() });
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceExpiryOrDetachAllowsAFreshSingleClockAdmission(bool detach)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var other = AddSecondHighscoreClock(db, game.Wired);
        var controller = HighscoreController(game.Wired);
        game.Actor.OnChat(0, "start", false);

        if (detach) {
            _room.RunFastPass(() => Assert.True(_room.GetRoomItemHandler().RemoveFurniture(_client, game.Timer)));
            Assert.Null(controller.ReadState(game.Timer));
        }
        else {
            _room.RunFastPass(() =>
            {
                Assert.True(controller.SetPulseCount(game.Timer, 0));
                controller.Poll(long.MaxValue); // Real expiry path, not a fabricated Running flag.
            });
            Assert.False(controller.IsRunning(game.Timer));
        }

        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        game.Actor.OnChat(0, "start-b", false);
        Assert.True(controller.IsRunning(other));
        _room.GetGameManager().Points[1] = 17;
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop-b", false);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
    }

    [WiredChestDatabaseFact]
    public void ActualModernResetBeforeEachSingletonRoundKeepsObservedBestAndWins()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        AddResetHighscoreActions();
        var controller = HighscoreController(game.Wired);
        var rounds = new[] { (Award: false, Seconds: 2, Wins: 0), (Award: true, Seconds: 3, Wins: 1), (Award: true, Seconds: 6, Wins: 2) };

        foreach (var round in rounds) {
            var prior = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
            game.Actor.OnChat(0, "reset-clock", false);
            Assert.False(controller.IsRunning(game.Timer));
            Assert.Equal(30000L, controller.ReadMilliseconds(game.Timer));
            Assert.Equal(prior, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray());
            game.Actor.OnChat(0, "begin-round", false);
            Assert.True(controller.IsRunning(game.Timer));
            Assert.Equal(0, _room.GetGameManager().Points[1]);

            if (round.Award) {
                game.Actor.OnChat(0, "award-points", false);
            }

            Assert.Equal(round.Award ? 17 : 0, _room.GetGameManager().Points[1]);
            game.Clock.Now += TimeSpan.FromSeconds(round.Seconds);
            game.Actor.OnChat(0, "stop", false);
            Assert.False(controller.IsRunning(game.Timer));
            var fastest = Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
            Assert.Equal(2, fastest.Score);
            Assert.Equal(new[] { game.Actor.GetUsername() }, fastest.Users);
            var wins = ((HighscoreDataFormat)game.Wins.ExtraData).Entries;

            if (round.Wins == 0) {
                Assert.Empty(wins);
            }
            else {
                Assert.Equal(round.Wins, Assert.Single(wins).Score);
            }

            Assert.Equal(new[] { game.Fastest.ExtraData.Serialize(), game.Wins.ExtraData.Serialize() },
                db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray());
        }
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PriorGuiOrUnknownResetCannotEnrollAModernProducerRound(bool gui)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var controller = HighscoreController(game.Wired);

        if (gui) {
            Assert.True(game.Wired.TryUseCounter(game.Timer, 2));
        }
        else {
            _room.RunFastPass(() => Assert.True(controller.Control(game.Timer, 2, game.Clock.Now.ToUnixTimeMilliseconds())));
        }

        Assert.Equal(30000L, controller.ReadMilliseconds(game.Timer));
        game.Actor.OnChat(0, "start", false);
        Assert.True(controller.IsRunning(game.Timer));
        Assert.Equal(17, _room.GetGameManager().Points[1]);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(controller.IsRunning(game.Timer));
        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnknownCompletionThenModernResetKeepsStartGatedAndFrozenBytesSafe(bool committed)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new PendingStartStore(new RoomHighscoreStore(db.Database), committed);
        var game = PrepareHighscoreGame(db, store);
        AddResetHighscoreActions();
        var controller = HighscoreController(game.Wired);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(1, store.Commits);
        var rows = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
        game.Actor.OnChat(0, "reset-clock", false);
        Assert.False(controller.IsRunning(game.Timer));
        var resetClock = (controller.ReadMilliseconds(game.Timer), ClockSequence(controller, game.Timer));
        var points = _room.GetGameManager().Points.ToArray();
        game.Actor.OnChat(0, "begin-round", false);
        Assert.False(controller.IsRunning(game.Timer));
        Assert.Equal(resetClock, (controller.ReadMilliseconds(game.Timer), ClockSequence(controller, game.Timer)));
        Assert.Equal(points, _room.GetGameManager().Points);
        Assert.Equal(rows, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray());
        Assert.False(_room.GetRoomItemHandler().TryReserveTransfers([game.Wins], out _));
        store.Available = true;
        game.Actor.OnChat(0, "begin-round", false);
        Assert.True(controller.IsRunning(game.Timer));
        Assert.Equal(0, _room.GetGameManager().Points[1]);
        Assert.Equal(1, store.Commits); // Reset invalidates all-prior retry eligibility; never recompute or retry that retired candidate.
        Assert.Equal(rows, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray());

        if (committed) {
            Assert.Equal(2, Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries).Score);
            Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        }
        else {
            Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
            Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        }

        Assert.True(_room.GetRoomItemHandler().TryReserveTransfers([game.Wins], out var reservation));
        _room.GetRoomItemHandler().CancelUnstartedTransfer(reservation!);
    }

    private void AddResetHighscoreActions()
    {
        AddSpeechBox(730, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "reset-clock" }, 0, 3, 3);
        AddSpeechBox(731, "wf_act_control_clock", new() { IntParams = [2, 100], SelectedItems = [503] }, 1, 3, 3);
        AddSpeechBox(740, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "begin-round" }, 0, 3, 0);
        AddSpeechBox(741, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [503] }, 1, 3, 0);
        AddSpeechBox(750, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "award-points" }, 0, 0, 3);
        AddSpeechBox(751, "wf_act_give_score_tm", new() { IntParams = [17, 0, 1] }, 1, 0, 3);
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedCompletionRefusesStartBeforeAnyClockOrScoreMutation(bool committed)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new PendingStartStore(new RoomHighscoreStore(db.Database), committed);
        var game = PrepareHighscoreGame(db, store);
        AddSpeechBox(730, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "restart" }, 0, 3, 3);
        AddSpeechBox(731, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [503] }, 1, 3, 3);
        var controller = HighscoreController(game.Wired);
        game.Actor.OnChat(0, "start", false);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(controller.IsRunning(game.Timer));
        Assert.Equal(1, store.Commits);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        var priorClock = (controller.ReadMilliseconds(game.Timer), ClockSequence(controller, game.Timer));
        var points = _room.GetGameManager().Points.ToArray();
        var rows = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray();
        game.Actor.OnChat(0, "restart", false);
        Assert.False(controller.IsRunning(game.Timer));
        Assert.Equal(priorClock, (controller.ReadMilliseconds(game.Timer), ClockSequence(controller, game.Timer)));
        Assert.Equal(points, _room.GetGameManager().Points);
        Assert.Equal(rows, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN(501,502) ORDER BY id").ToArray());
        Assert.False(_room.GetRoomItemHandler().TryReserveTransfers([game.Wins], out _));
        store.Available = true;
        game.Actor.OnChat(0, "restart", false);
        Assert.True(controller.IsRunning(game.Timer));
        Assert.Equal(0, _room.GetGameManager().Points[1]);
        Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        Assert.Equal(committed ? 1 : 2, store.Commits);
    }

    private sealed class PendingStartStore(IRoomHighscoreStore inner, bool committed) : IRoomHighscoreStore
    {
        public bool Available;
        public int Commits;
        public bool Commit(IReadOnlyList<HighscoreWrite> writes)
        {
            Commits++;

            if (Commits == 1) {
                if (committed) {
                    Assert.True(inner.Commit(writes));
                }

                throw new IOException(committed ? "actual COMMIT acknowledgement lost" : "commit unavailable before SQL");
            }

            return inner.Commit(writes);
        }
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes) => Commits == 0 || Available
            ? inner.Read(writes) : throw new IOException("pending completion read unavailable");
    }

    private static Plus.HabboHotel.Items.Wired.Modern.Actions.WiredCounterController HighscoreController(Plus.HabboHotel.Rooms.Instance.WiredComponent wired) =>
        (Plus.HabboHotel.Items.Wired.Modern.Actions.WiredCounterController)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
    private static long ClockSequence(Plus.HabboHotel.Items.Wired.Modern.Actions.WiredCounterController controller, Item timer)
    {
        var clocks = (System.Collections.IDictionary)controller.GetType().GetField("_clocks", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(controller)!;
        var clock = clocks[timer.Id]!;

        return (long)clock.GetType().GetField("Sequence")!.GetValue(clock)!;
    }
    private Item AddSecondHighscoreClock(WiredChestDatabaseTests.Fixture db, Plus.HabboHotel.Rooms.Instance.WiredComponent wired)
    {
        var other = HighscoreTimer();
        other.Id = 504;
        other.Definition.Id = 504;
        _room.RunFastPass(() => Assert.True(_room.GetRoomItemHandler().SetFloorItem(null!, other, 1, 0, 0, true, false, false)));
        db.Connection.Execute("INSERT INTO items(id,user_id,room_id,base_item,extra_data) VALUES(504,7,42,504,'30')");
        AddSpeechBox(720, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "start-b" }, 0, 3, 0);
        AddSpeechBox(721, "wf_act_control_clock", new() { IntParams = [0, 100], SelectedItems = [504] }, 1, 3, 0);
        AddSpeechBox(722, "wf_trg_says_something", new() { IntParams = [1, 0, 0], Text = "stop-b" }, 0, 0, 3);
        AddSpeechBox(723, "wf_act_control_clock", new() { IntParams = [1, 100], SelectedItems = [504] }, 1, 0, 3);

        return other;
    }

    [WiredChestDatabaseFact]
    public void ActualStopFreezesNoncanonicalDurablePriorAndKeepsUnchangedRawBytes()
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        var score = _room.GetRoomItemHandler().GetItem(304)!;
        Assert.True(_room.GetWired().TryRemove(score.Id)); // Zero score changes fastest but leaves wins unchanged.
        var raw = new[] { game.Fastest, game.Wins }.Select(board => System.Text.Json.JsonSerializer.Serialize(new
        {
            Entries = Array.Empty<object>(),
            ClearType = 0,
            ScoreType = ((HighscoreDataFormat)board.ExtraData).ScoreType,
            State = "0",
            Version = 1
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true })).ToArray();

        for (var i = 0; i < 2; i++) {
            var board = i == 0 ? game.Fastest : game.Wins;
            Assert.NotEqual(board.ExtraData.Serialize(), raw[i]);
            var parsed = new HighscoreDataFormat();
            parsed.Store(raw[i]);
            Assert.Equal(board.ExtraData.Serialize(), parsed.Serialize());
            db.Connection.Execute("UPDATE items SET extra_data=@payload WHERE id=@ItemId", new { payload = raw[i], ItemId = board.Id });
        }

        game.Actor.OnChat(0, "start", false);
        Assert.True(_room.GetSoccer().GameIsStarted);
        Assert.Equal(0, _room.GetGameManager().Points[1]);
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Equal(2, Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries).Score);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        Assert.Equal(raw[1], db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=502"));
        Assert.Equal(game.Fastest.ExtraData.Serialize(), db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=501"));
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(raw[1], db.Connection.QuerySingle<string>("SELECT extra_data FROM items WHERE id=502"));
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedStopCaptureRefusesBeforeAnyWriteOrReservation(bool unavailableRead)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var store = new CaptureRefusalStore(new RoomHighscoreStore(db.Database), unavailableRead);
        var game = PrepareHighscoreGame(db, store);
        game.Actor.OnChat(0, "start", false);
        Assert.True(_room.GetSoccer().GameIsStarted);

        if (!unavailableRead) {
            var changed = new HighscoreDataFormat { State = "1", ScoreType = 1, ClearType = 0 };
            db.Connection.Execute("UPDATE items SET extra_data=@payload WHERE id=502", new { payload = changed.Serialize() });
        }

        var prior = db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN (501,502) ORDER BY id").ToArray();
        game.Clock.Now += TimeSpan.FromSeconds(2);
        game.Actor.OnChat(0, "stop", false);
        Assert.False(_room.GetSoccer().GameIsStarted);
        Assert.Equal(1, store.Reads);
        Assert.Equal(0, store.Commits);
        Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
        Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        Assert.Equal(prior, db.Connection.Query<string>("SELECT extra_data FROM items WHERE id IN (501,502) ORDER BY id").ToArray());
        var handler = _room.GetRoomItemHandler();
        Assert.True(handler.TryReserveTransfers([game.Fastest, game.Wins], out var reservation));
        handler.CancelUnstartedTransfer(reservation!);
        game.Actor.OnChat(0, "stop", false);
        Assert.Equal(1, store.Reads);
        Assert.Equal(0, store.Commits);
    }

    private sealed class CaptureRefusalStore(IRoomHighscoreStore inner, bool unavailableRead) : IRoomHighscoreStore
    {
        public int Reads;
        public int Commits;
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes)
        {
            Reads++;

            return unavailableRead ? throw new IOException("initial durable capture unavailable; no COMMIT attempted") : inner.Read(writes);
        }
        public bool Commit(IReadOnlyList<HighscoreWrite> writes)
        {
            Commits++;

            return inner.Commit(writes);
        }
    }

    [WiredChestDatabaseTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactActorHabboIdMustStayUnchangedForTheExplicitRound(bool changeId)
    {
        using var db = new WiredChestDatabaseTests.Fixture();
        var game = PrepareHighscoreGame(db);
        game.Actor.OnChat(0, "start", false);
        Assert.True(_room.GetSoccer().GameIsStarted);
        var originalId = game.Actor.HabboId;

        if (changeId) {
            game.Actor.HabboId = originalId + 1;
        }

        Assert.Equal("owner", game.Actor.GetUsername());
        game.Clock.Now += TimeSpan.FromSeconds(2);
        // The second real speech admission supplies the same actor directly, avoiding a different-ID lookup failure.
        game.Wired.Dispatch(new(Plus.HabboHotel.Items.Wired.Runtime.WiredEventKind.Speech) { Actor = game.Actor, Message = "stop" });
        Assert.False(_room.GetSoccer().GameIsStarted);

        if (changeId) {
            Assert.Empty(((HighscoreDataFormat)game.Fastest.ExtraData).Entries);
            Assert.Empty(((HighscoreDataFormat)game.Wins.ExtraData).Entries);
        }
        else {
            Assert.Equal(2, Assert.Single(((HighscoreDataFormat)game.Fastest.ExtraData).Entries).Score);
            Assert.Equal(1, Assert.Single(((HighscoreDataFormat)game.Wins.ExtraData).Entries).Score);
        }
    }

    private sealed class CountingHighscoreStore(IRoomHighscoreStore inner) : IRoomHighscoreStore
    {
        public int Commits;
        public bool Commit(IReadOnlyList<HighscoreWrite> writes)
        {
            Commits++;

            return inner.Commit(writes);
        }
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes) => inner.Read(writes);
    }

    private Room HighscoreOtherRoom(WiredChestDatabaseTests.Fixture db, uint id)
    {
        var room = (Room)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Room));

        foreach (var field in typeof(Room).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)) {
            field.SetValue(room, field.GetValue(_room));
        }

        room.Id = id;
        void SetOther(string name, object? value) => typeof(Room).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, value);
        SetOther("_gameManager", null);
        SetOther("_gamemap", new Gamemap(room, new RoomModel("second", 0, 0, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false),
            TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance));
        SetOther("_roomItemHandling", new RoomItemHandling(room, new RoomItemStore(db.Database), new RoomItemMetadataStore(db.Database),
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards));
        SetOther("_roomUserManager", new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, TestRewardProgress.Unused,
            TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        SetOther("_wiredComponent", new Plus.HabboHotel.Rooms.Instance.WiredComponent(room, TestLogging.Logger, TimeProvider.System,
            TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, new EmptyConfigurationStore(), db.Database,
            TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty,
            TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel));
        room.GetGameMap().GenerateMaps();

        return room;
    }

    private sealed class UnavailableAfterCommitStore(IRoomHighscoreStore inner) : IRoomHighscoreStore
    {
        public bool Committed;
        public bool Commit(IReadOnlyList<HighscoreWrite> writes)
        {
            Assert.True(inner.Commit(writes));
            Committed = true;
            throw new IOException("acknowledgement lost AFTER real COMMIT");
        }
        public IReadOnlyList<HighscoreRow> Read(IReadOnlyList<HighscoreWrite> writes) => !Committed ? inner.Read(writes) : throw new IOException("fresh reconciliation unavailable");
    }

    private Item HighscoreBoard(uint id, uint kind) => new()
    {
        Id = id,
        RoomId = RoomId,
        OwnerId = 7,
        UserId = 7,
        Definition = new()
        {
            Id = id,
            ItemName = kind == 3 ? "highscore_fastesttime*1" : "highscore_mostwin*1",
            Type = ItemType.Floor,
            Width = 1,
            Length = 1,
            Stackable = true,
            Height = 1,
            InteractionType = InteractionType.None
        },
        ExtraData = new HighscoreDataFormat { State = "0", ScoreType = kind }
    };

    private static Item HighscoreTimer() => new()
    {
        Id = 503,
        RoomId = RoomId,
        OwnerId = 7,
        UserId = 7,
        Definition = new()
        {
            Id = 503,
            ItemName = "fball_counter",
            InteractionName = "game_timer",
            InteractionType = InteractionType.Counter,
            Type = ItemType.Floor,
            Width = 1,
            Length = 1,
            Stackable = true,
            Height = 1
        },
        ExtraData = new LegacyDataFormat { Data = "30" }
    };

    private sealed class HighscoreClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.FromUnixTimeMilliseconds(1000);
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Now.ToUnixTimeMilliseconds();
        public override long TimestampFrequency => 1000;
    }
}

public sealed class WiredChestDatabaseTheoryAttribute : TheoryAttribute
{
    public WiredChestDatabaseTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("WIRED_CHEST_DATABASE") == null) {
            Skip = "Set WIRED_CHEST_DATABASE to a disposable loopback MariaDB server.";
        }
    }
}
