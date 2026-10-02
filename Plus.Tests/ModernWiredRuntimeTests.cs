using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.Database.Interfaces;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[CollectionDefinition("Modern Wired database seam", DisableParallelization = true)]
public sealed class ModernWiredDatabaseCollection;

[Collection("Modern Wired database seam")]
public class ModernWiredRuntimeTests
{
    private static WiredModernAction ActionBox(Room room, string name, WiredCounterController? clocks = null, WiredRoomLog? log = null) =>
        new(room, MakeItem(100, name), Descriptor(name), clocks ?? new(), _ => { }, (_, _, _) => { }, log ?? new());

    [Fact]
    public void AllImplementedEditorsHaveValidatedDefaults()
    {
        var (room, _, _) = World();
        foreach (var name in WiredTriggerConfiguration.Events.Keys)
            Assert.True(WiredTriggerConfiguration.TryValidate(name, WiredTriggerConfiguration.Defaults(name), out _, out _), name);
        foreach (var name in WiredConditionConfiguration.PositiveNames.Concat(WiredConditionConfiguration.NegativeNames.Keys))
            Assert.True(WiredConditionConfiguration.TryValidate(name, WiredConditionConfiguration.Defaults(name), out _, out _), name);
        foreach (var name in WiredMovementActions.Names.Concat(WiredModernAction.OtherNames))
            Assert.True(ActionBox(room, name).TryValidateConfiguration(WiredActionConfiguration.Defaults(name), out _, out _), name);
    }

    [Fact]
    public void NegativeStackAndSplitSignalsCallRealOperationsWithSeparateRoles()
    {
        var (room, _, _) = World();
        var antenna = MakeItem(1, "antenna"); var forwarded = MakeItem(2, "forwarded");
        var clicked = new RoomUser(1, 0, 7, room);
        var operations = new RecordingOperations();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.ClickUser) { TargetUser = clicked },
            new(() => new[] { antenna, forwarded }, () => new[] { clicked }), operations);
        var call = ActionBox(room, "wf_act_neg_call_stacks");
        call.Item.SetState(2, 2, 0, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
        Assert.True(call.TryValidateConfiguration(new() { IntParams = [100], SelectedItems = [1] }, out var callConfig, out _));
        call.ApplyConfiguration(callConfig);
        Assert.True(call.IsNegative); Assert.True(call.Execute(context));
        Assert.True(operations.CallNegative); Assert.Equal(new uint[] { 1 }, operations.Called);
        call.Item.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        Assert.False(call.Execute(context)); Assert.Empty(operations.Called);
        var signal = ActionBox(room, "wf_act_neg_send_signal");
        Assert.True(signal.TryValidateConfiguration(new() { IntParams = [1, 100, 11, 1, 1, 0], SelectedItems = [1], Text = "2" }, out var signalConfig, out _));
        signal.ApplyConfiguration(signalConfig); Assert.True(signal.Execute(context));
        var received = Assert.Single(operations.Signals);
        Assert.True(received.Negative); Assert.Equal(new uint[] { 1 }, received.Receivers);
        Assert.Equal(new uint[] { 2 }, received.Selection.FurniIds); Assert.Equal(new[] { 7 }, received.Selection.UserIds);
    }

    [Fact]
    public void ConfiguredClockActionControlsActualAttachedClock()
    {
        var (room, _, _) = World(); var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController(); clocks.Attach(item);
        var box = ActionBox(room, "wf_act_adjust_clock", clocks);
        box.TryValidateConfiguration(new() { IntParams = [2, 100, 1, 3], SelectedItems = [1] }, out var config, out _);
        box.ApplyConfiguration(config); Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [item], [])));
        Assert.Equal(61500, clocks.ReadMilliseconds(item)); Assert.False(clocks.HasRunning);
        clocks.Control(item, 0, 0); Assert.True(clocks.HasRunning); clocks.Forget(item); Assert.False(clocks.HasRunning);
    }

    [Fact]
    public void LogActionWritesBoundedRoomMonitorAndEmptyTextHasNoEffect()
    {
        var (room, _, _) = World(); var log = new WiredRoomLog(2);
        var box = ActionBox(room, "wf_act_neg_log", log: log);
        box.TryValidateConfiguration(new() { IntParams = [1, 0], Text = "First" }, out var config, out _);
        box.ApplyConfiguration(config); Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.Equal("First", Assert.Single(log.Read(0, 10).Entries).Message);
        log.Append(2, 100, "Second", DateTimeOffset.UtcNow); log.Append(1, 100, "Third", DateTimeOffset.UtcNow);
        Assert.Equal(2, log.Read(0, 10).Total); Assert.Equal("Third", Assert.Single(log.Read(0, 10, 1, "third").Entries).Message);
        box.ApplyConfiguration(config with { Text = "" }); Assert.False(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
    }

    [Fact]
    public void LegacyEditorConversionPreservesSavedSnapshotAndPlaceholderText()
    {
        var (room, _, _) = World(); var picked = MakeItem(1, "test");
        var old = new MatchPositionBox(room, MakeItem(100, "wf_act_match_to_sshot"))
            { StringData = "1;1;1", ItemsData = "1:2,1,3.25,4,old,raw,state" };
        old.SetItems[1] = picked; picked.LegacyDataString = "changed";
        Assert.True(WiredLegacyConfigurationAdapter.TryConvert(old, Descriptor("wf_act_match_to_sshot"), out var config));
        Assert.Equal("old,raw,state", Assert.Single(config.Snapshots).State); Assert.Equal(3.25, config.Snapshots[0].Z);
        var chat = new ShowMessageBox(room, MakeItem(101, "wf_act_show_message")) { StringData = "Hello %USERNAME%" };
        Assert.True(WiredLegacyConfigurationAdapter.TryConvert(chat, Descriptor("wf_act_show_message"), out var converted));
        Assert.Equal(new[] { 0, 0, 34, -1 }, converted.IntParams); Assert.Equal(chat.StringData, converted.Text);
    }

    [Fact]
    public void ActiveChatAndMovementComposersPreserveParserFields()
    {
        var fields = new List<object>(); var packet = DispatchProxy.Create<IOutgoingPacket, RecordingProxy>();
        ((RecordingProxy)(object)packet).InvokeMethod = (_, args) => { fields.Add(args![0]!); return null; };
        new WiredChatComposer(7, "Hello", 252, 2, true).Compose(packet);
        Assert.Equal(new object[] { 7, "Hello", 0, 252, 0, "", 5, "", "", "", "", "", "", "icon-prefix-name", 2 }, fields);
        fields.Clear(); new WiredMovementComposer(1, 10, 0, 1, 1.25, 2, 2, 3.5, 4, 4, 750).Compose(packet);
        Assert.Equal(new object[] { 1, 1, 0, 1, 2, 2, "1.25", "3.5", 10, 4, 750, 0, 0, 0 }, fields);
        var (room, _, _) = World(); var box = ActionBox(room, "wf_act_show_message");
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [0, 0, 252, 2], Text = "Hello" }, out var config, out _));
        Assert.Equal(2, config.IntParams[3]);
        Assert.False(box.TryValidateConfiguration(config with { IntParams = [0, 0, 252, 3] }, out _, out _));
    }

    private sealed class RecordingOperations : IWiredRuntimeOperations
    {
        public uint[] Called = []; public bool CallNegative;
        public List<(uint[] Receivers, WiredSelection Selection, bool Negative)> Signals = [];
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false)
        { Called = targets.Select(x => x.Id).ToArray(); CallNegative = negative; return Called.Length > 0; }
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false)
        { Signals.Add((receivers.Select(x => x.Id).ToArray(), selection.Copy(), negative)); return true; }
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    [Fact]
    public void TemporaryEffectLeasesPreserveNewEffectsOverlapsAndRoomVisitIdentity()
    {
        var (room, _, _) = World(); var user = new RoomUser(1, 0, 7, room);
        var effects = new WiredTemporaryEffects(); var current = 8; var attached = true;
        var first = effects.Acquire(user, () => current, value => current = value, () => attached);
        var second = effects.Acquire(user, () => current, value => current = value, () => attached);
        first(); Assert.Equal(4, current); second(); Assert.Equal(8, current); second(); Assert.Equal(8, current);
        var changed = effects.Acquire(user, () => current, value => current = value, () => attached);
        current = 12; changed(); Assert.Equal(12, current);
        var departed = effects.Acquire(user, () => current, value => current = value, () => attached);
        attached = false; current = -1; departed(); Assert.Equal(-1, current);
        attached = true; var capFailure = effects.Acquire(user, () => current, value => current = value, () => attached);
        capFailure(); Assert.Equal(-1, current); // Immediate cleanup when engine refuses the restore callback.
    }

    [Fact]
    public void HeadingRemembersTurnAndStopDoesNotInventMovement()
    {
        var item = MakeItem(1, "test"); var directions = new WiredDirectionalActions(); var attempts = new List<Point>();
        Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return x == 1; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new[] { new Point(0, -1), new Point(1, 0) }, attempts);
        attempts.Clear(); Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return true; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new Point(1, 0), Assert.Single(attempts));
        attempts.Clear(); Assert.False(new WiredDirectionalActions().MoveHeading(item, 0, 6, false,
            (x, y) => { attempts.Add(new(x, y)); return false; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Single(attempts);
        Assert.Equal(2, WiredDirectionalActions.AvatarRotation(0, 8)); Assert.Equal(6, WiredDirectionalActions.AvatarRotation(0, 9));
    }

    [Fact]
    public void ChaseQueriesNearestWithinThreeAndOrdersLongAxisFirst()
    {
        var (room, _, _) = World(); var item = MakeItem(1, "test");
        var near = new RoomUser(1, 0, 7, room) { X = 2, Y = 1 }; var far = new RoomUser(2, 0, 8, room) { X = 4, Y = 0 };
        Assert.Same(near, WiredDirectionalActions.Nearest(item, [far, near])); Assert.Null(WiredDirectionalActions.Nearest(item, [far]));
        Assert.Equal(new[] { new Point(1, 0), new Point(0, 1) }, WiredDirectionalActions.Steps(item, near, false));
        Assert.Equal(new[] { new Point(-1, 0), new Point(0, -1) }, WiredDirectionalActions.Steps(item, near, true));
    }

    [Fact]
    public void WiredFreezePreservesExistingGameFreezeAndConsumesTeleportCancelFlag()
    {
        var (room, _, _) = World(); var user = new RoomUser(1, 0, 7, room); var state = new WiredAvatarState();
        user.SetStatus("mv", "1,1,0"); user.IsWalking = true;
        Assert.True(state.FreezeUser(user, 0, false)); Assert.True(user.Frozen); Assert.False(user.CanWalk); Assert.False(user.IsWalking); Assert.False(user.HasStatus("mv"));
        Assert.False(state.Thaw(user, teleport: true)); Assert.True(user.Frozen);
        state.FreezeUser(user, 0, true); Assert.True(state.Thaw(user, teleport: true)); Assert.False(user.Frozen); Assert.True(user.CanWalk);
        user.Frozen = true; user.CanWalk = false; state.FreezeUser(user, 0, true); state.Thaw(user);
        Assert.True(user.Frozen); Assert.False(user.CanWalk); // A game freeze is independently owned.
    }

    [Theory]
    [InlineData(4, 0, 1)] [InlineData(6, 0, -1)] [InlineData(8, 1, -1)] [InlineData(11, -1, -1)]
    public void FurnitureMoveUsesActualPolarisDirectionNumbers(int raw, int dx, int dy)
    {
        var item = MakeItem(1, "test"); var moves = new List<Point>();
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [raw, 0, 100] }, [item], [], [],
            (_, x, y, _, _) => { moves.Add(new(x, y)); return true; }, (_, _, _, _, _) => false, (_, _) => throw new Exception()));
        Assert.Equal(new Point(dx, dy), Assert.Single(moves));
    }
    [Fact]
    public void ClockTicksAtHalfSecondsAndDisplaysOnlyWholeSeconds()
    {
        var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController();
        Assert.True(clocks.Attach(item));
        Assert.True(clocks.Control(item, 0, 100));
        Assert.Empty(clocks.Poll(599));
        var half = Assert.Single(clocks.Poll(600));
        Assert.Equal(500, half.Event.Value);
        Assert.False(half.DisplayChanged);
        var whole = Assert.Single(clocks.Poll(1100));
        Assert.Equal(500, whole.Event.PreviousValue);
        Assert.Equal(1000, whole.Event.Value);
        Assert.Equal("1", item.LegacyDataString);
        Assert.True(whole.DisplayChanged);
        Assert.Single(clocks.Poll(10000)); // Late polls never burst overdue ticks.
        Assert.Equal(1500, clocks.ReadMilliseconds(item));
    }

    [Fact]
    public void ClockAdjustTranslatesPolarisOperatorsAndClampsAtBothBounds()
    {
        var item = MakeItem(1, "wf_upcounter2");
        var clocks = new WiredCounterController(10);
        clocks.Attach(item);
        Assert.True(clocks.Adjust(item, 2, 0, 7));
        Assert.Equal(3500, clocks.ReadMilliseconds(item));
        clocks.Adjust(item, 0, 0, 5);
        Assert.Equal(5000, clocks.ReadMilliseconds(item));
        clocks.Adjust(item, 1, 0, 119);
        Assert.Equal(0, clocks.ReadMilliseconds(item));
        Assert.False(clocks.Adjust(item, 3, 0, 0));
    }

    [Fact]
    public void ClockStopResetRestartAndRemovalHaveActualLifecycleState()
    {
        var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        clocks.Control(item, 0, 0);
        clocks.Poll(500);
        clocks.Control(item, 1, 500);
        Assert.Empty(clocks.Poll(5000));
        Assert.Equal(500, clocks.ReadMilliseconds(item));
        clocks.Control(item, 3, 5000);
        Assert.True(clocks.IsRunning(item));
        Assert.Equal(0, clocks.ReadMilliseconds(item));
        clocks.TakeChanges();
        Assert.Equal(500, Assert.Single(clocks.Poll(5500)).Event.Value);
        clocks.Control(item, 2, 5500);
        Assert.False(clocks.IsRunning(item));
        clocks.Forget(item);
        Assert.Null(clocks.ReadMilliseconds(item));
        Assert.Empty(clocks.Poll(10000));
        Assert.False(clocks.Control(item, 0, 10000));
    }

    [Fact]
    public void GameCounterCountsDownAndPublishesStartEndExactlyOnce()
    {
        var item = MakeItem(1, "wf_game_upcounter1");
        item.LegacyDataString = "2";
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        Assert.True(clocks.Use(item, 0, 0));
        Assert.Equal(WiredEventKind.GameStart, Assert.Single(clocks.TakeChanges()).Event.Kind);
        Assert.False(clocks.Control(item, 0, 0)); // Wired clock controls target upcounters, not game countdowns.
        Assert.Empty(clocks.Poll(999));
        Assert.Equal("1", Assert.Single(clocks.Poll(1000)).Item.LegacyDataString);
        Assert.Equal(new[] { WiredEventKind.StateChanged, WiredEventKind.GameEnd }, clocks.Poll(2000).Select(change => change.Event.Kind));
        Assert.False(clocks.IsRunning(item));
        Assert.Empty(clocks.Poll(3000));
        clocks.Use(item, 2, 3000);
        Assert.Equal("60", item.LegacyDataString);
        Assert.Equal(WiredEventKind.StateChanged, Assert.Single(clocks.TakeChanges()).Event.Kind);
    }

    [Fact]
    public void ControllerRejectsUnrelatedItemsAndReusedIds()
    {
        var clocks = new WiredCounterController();
        Assert.False(clocks.Attach(MakeItem(1, "football_counter")));
        var original = MakeItem(1, "wf_upcounter1");
        clocks.Attach(original);
        Assert.Null(clocks.ReadMilliseconds(MakeItem(1, "wf_upcounter1")));
    }

    [Fact]
    public void ConditionUsesClickedAvatarSourceAndPolarisActionNumbers()
    {
        var (room, _, _) = World();
        var user = new RoomUser(1, 0, 7, room);
        user.SetStatus("sit");
        var context = Context(room, new(WiredEventKind.ClickUser) { TargetUser = user }, [], [user]);
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_user_performs_action"), Descriptor("wf_cnd_user_performs_action"), _ => null, () => DateTimeOffset.UtcNow);
        Assert.True(box.TryValidateConfiguration(new() { IntParams = [6, 0, 0, 0, 1, 11, 0] }, out var config, out _));
        box.ApplyConfiguration(config);
        Assert.True(box.Execute(context));
        user.RemoveStatus("sit");
        Assert.False(box.Execute(context));
    }

    [Fact]
    public void ConditionConfigurationKeepsPrimaryAndComparisonRoles()
    {
        var proposed = new WiredConfiguration { IntParams = [100, 201, 0], Text = "5;6", SelectedItems = [1] };
        Assert.True(WiredConditionConfiguration.TryValidate("wf_cnd_stuff_is", proposed, out var result, out _));
        Assert.Equal(100, result.FurniSources["items"]);
        Assert.Equal(201, result.FurniSources["comparison"]);
        Assert.Equal(new uint[] { 5, 6 }, result.SecondarySelectedItems);
        Assert.Equal(proposed.IntParams, result.IntParams);
        Assert.Equal(proposed.Text, result.Text);
    }

    [Fact]
    public void SnapshotPreparationCapturesOnlyPickedAttachedItemsWithoutMutatingLiveConfig()
    {
        var (room, _, items) = World();
        var picked = MakeItem(1, "test");
        picked.LegacyDataString = "state,with;delimiters";
        items[1] = picked;
        items[2] = MakeItem(2, "test");
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_match_snapshot"), Descriptor("wf_cnd_match_snapshot"), _ => null, () => DateTimeOffset.UtcNow);
        var candidate = new WiredConfiguration { IntParams = [1, 1, 1, 1, 100, 0], SelectedItems = [1] };
        var prepared = WiredRoomOperations.PrepareSnapshots(box, candidate);
        Assert.Equal("state,with;delimiters", Assert.Single(prepared.Snapshots).State);
        Assert.Empty(candidate.Snapshots);
        Assert.Empty(box.Configuration.Snapshots);
    }

    [Fact]
    public void TriggerSeparatesUseAndStateMutationAndUsesStoredSnapshot()
    {
        var (room, _, _) = World();
        var item = MakeItem(1, "test");
        var box = new WiredModernTrigger(room, MakeItem(100, "wf_trg_state_changed"), Descriptor("wf_trg_state_changed"));
        box.TryValidateConfiguration(new() { IntParams = [1, 100], SelectedItems = [1], Snapshots = [WiredRoomOperations.Capture(item)] }, out var config, out _);
        box.ApplyConfiguration(config);
        Assert.False(box.Execute(Context(room, new(WiredEventKind.Use) { EventItem = item }, [item], [])));
        Assert.True(box.Execute(Context(room, new(WiredEventKind.StateChanged) { EventItem = item }, [item], [])));
        item.LegacyDataString = "1";
        Assert.False(box.Execute(Context(room, new(WiredEventKind.StateChanged) { EventItem = item }, [item], [])));
    }

    [Fact]
    public void TimedContextualTriggerResetsItsOneShotEpoch()
    {
        var (room, _, _) = World();
        var box = new WiredModernTimedTrigger(room, MakeItem(100, "wf_trg_at_time_long"), Descriptor("wf_trg_at_time_long"));
        box.TryValidateConfiguration(new() { IntParams = [1] }, out var config, out _);
        box.ApplyConfiguration(config);
        box.Reset(1000);
        Assert.Null(box.Poll(5999));
        Assert.Equal(WiredEventKind.Elapsed, box.Poll(6000)!.Kind);
        Assert.Null(box.Poll(11000));
        box.Reset(11000);
        Assert.NotNull(box.Poll(16000));
    }

    [Fact]
    public void FullPlacementHonoursScopedUsersAndPreservesOrdinaryRejection()
    {
        var (room, map, items) = World();
        var mover = MakeItem(1, "test");
        mover.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var occupant = new RoomUser(1, 0, 7, room) { X = 1, Y = 1 };
        map.AddUserToMap(occupant, new(1, 1));
        Assert.False(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false));
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: new(new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint>())));
        var allowed = new WiredCollisionPolicy(new HashSet<uint>(), new HashSet<int> { 7 }, new HashSet<uint>());
        Assert.True(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: allowed));
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var queries = new List<string>();
        var db = DispatchProxy.Create<IDatabase, RecordingProxy>();
        var adapter = DispatchProxy.Create<IQueryAdapter, RecordingProxy>();
        ((RecordingProxy)(object)adapter).InvokeMethod = (method, args) => { if (method.Name == "RunQuery" && args?.Length == 1) queries.Add((string)args[0]!); return null; };
        ((RecordingProxy)(object)db).InvokeMethod = (method, _) => method.Name == "GetQueryReactor" ? adapter : null;
        try
        {
            databaseField.SetValue(null, db);
            Assert.True(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false, wiredCollision: allowed));
            Assert.Equal(new Point(1, 1), new Point(mover.GetX, mover.GetY));
            Assert.DoesNotContain(mover, map.GetCoordinatedItems(new(0, 0)));
            Assert.Contains(mover, map.GetCoordinatedItems(new(1, 1)));
            Assert.Contains(queries, query => query.Contains("UPDATE `items`"));
        }
        finally { databaseField.SetValue(null, original); }
    }

    [Fact]
    public void PhysicsScopeNeverPermitsAnExplicitBlockingFurnitureItem()
    {
        var (room, map, items) = World();
        var mover = MakeItem(1, "test");
        items[1] = mover;
        var obstacle = MakeItem(2, "test");
        obstacle.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        items[2] = obstacle;
        map.AddToMap(obstacle);
        var blocked = new WiredCollisionPolicy(new HashSet<uint> { 2 }, new HashSet<int>(), new HashSet<uint> { 2 });
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: blocked));
        Assert.False(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false, wiredCollision: blocked));
    }

    public class RecordingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMethod = (_, _) => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
    }
    private static WiredBoxDescriptor Descriptor(string name) { Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor)); return descriptor; }
    private static Item MakeItem(uint id, string name) => new()
    {
        Id = id, ExtraData = new LegacyDataFormat { Data = "0" }, Definition = new() { Type = ItemType.Floor, ItemName = name, InteractionName = name,
            Width = 1, Length = 1, Modes = 2, AdjustableHeights = [], VendingIds = [], PublicName = name }
    };
    private static (Room Room, Gamemap Map, ConcurrentDictionary<uint, Item> Items) World()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("wired-test", 0, 0, 0, 0, "000\r000\r000", false, 0, true));
        var handler = new RoomItemHandling(room);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room));
        typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[3, 3]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[3, 3]);
        return (room, map, (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!);
    }
    private static WiredRuntimeContext Context(Room room, WiredRuntimeEvent @event, Item[] items, RoomUser[] users) =>
        new(room, @event, new(() => items, () => users), new UnusedOperations());
    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
