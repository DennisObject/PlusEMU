using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Rooms.Games.Teams;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Effects;
using Plus.Communication.Flash;
using Plus.Communication.Revisions;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;
using Dapper;
using MySqlConnector;
using Plus.HabboHotel.Users.Inventory;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Settings;
using Plus.Communication.Packets.Incoming.Rooms.Furni.Wired;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;

namespace Plus.Tests;

[CollectionDefinition("Modern Wired database seam", DisableParallelization = true)]
public sealed class ModernWiredDatabaseCollection;

[Collection("Modern Wired database seam")]
public class ModernWiredRuntimeTests
{
    private static WiredModernAction ActionBox(Room room, string name, WiredCounterController? clocks = null, WiredRoomLog? log = null,
        TimeProvider? clock = null, IWiredRewardService? rewards = null, IItemDataManager? definitions = null, Action<WiredRuntimeEvent>? publish = null)
    {
        var item = MakeItem(100, name);
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, room);
        item.RoomId = room.Id;
        return new(room, item, Descriptor(name), clocks ?? new(), publish ?? (_ => { }), (_, _, _) => { }, log ?? new(), TestLogging.Logger,
            clock ?? TimeProvider.System, rewards ?? TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, definitions ?? TestWiredDefinitions.Unused, TestItemRuntime.Travel);
    }

    [Fact]
    public void ElapsedConditionsStartTheRoomTimerOnFirstUseAndShareItsEpoch()
    {
        var (room, _, _) = World();
        var instant = new DateTimeOffset(2040, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var now = instant.ToOffset(TimeSpan.FromHours(9));
        var less = Condition("wf_cnd_time_less_than");
        var more = Condition("wf_cnd_time_more_than");

        Assert.True(less.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.Equal(instant, room.LastTimerResetAt);
        Assert.False(more.Execute(Context(room, new(WiredEventKind.Use), [], [])));

        now = instant.AddMilliseconds(999).ToOffset(TimeSpan.FromHours(-7));
        Assert.True(less.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.False(more.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        now = instant.AddMilliseconds(1001);
        Assert.False(less.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.False(more.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        now = instant.AddMilliseconds(1500);
        Assert.True(more.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.Equal(instant, room.LastTimerResetAt);

        WiredModernCondition Condition(string name)
        {
            var condition = new WiredModernCondition(room, MakeItem(101, name), Descriptor(name),
                TestGroupManager.Empty, _ => null, () => now);
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(condition, new() { IntParams = [2] }, out var configuration, out var error), error);
            condition.ApplyConfiguration(configuration);

            return condition;
        }
    }

    [Fact]
    public void TimerResetAndElapsedConditionsUseCapturedUtcInstantsAtExactBoundaries()
    {
        var (room, _, _) = World();
        var instant = new DateTimeOffset(2040, 4, 5, 6, 7, 8, TimeSpan.Zero);
        var clock = new CountingClock(instant, TimeZoneInfo.CreateCustomTimeZone("wired-plus-nine", TimeSpan.FromHours(9), "test", "test"));
        var action = ActionBox(room, "wf_act_reset_timers", clock: clock);
        WiredNativeTestSupport.InstallRuntime(action, WiredActionConfiguration.Defaults("wf_act_reset_timers"));
        var operations = new ResetOperations();

        Assert.True(action.Execute(new WiredRuntimeContext(room, new(WiredEventKind.Use), new(() => [], () => []), operations)));
        Assert.Equal(instant, room.LastTimerResetAt);
        Assert.Equal(1, clock.Calls);
        Assert.Equal(1, operations.Resets);

        AssertBoundary("wf_cnd_time_less_than", instant.AddMilliseconds(999).ToOffset(TimeSpan.FromHours(9)), true);
        AssertBoundary("wf_cnd_time_less_than", instant.AddSeconds(1).ToOffset(TimeSpan.FromHours(9)), false);
        AssertBoundary("wf_cnd_time_more_than", instant.AddSeconds(1).ToOffset(TimeSpan.FromHours(-7)), false);
        AssertBoundary("wf_cnd_time_more_than", instant.AddMilliseconds(1001).ToOffset(TimeSpan.FromHours(-7)), false);
        AssertBoundary("wf_cnd_time_more_than", instant.AddMilliseconds(1499).ToOffset(TimeSpan.FromHours(-7)), false);
        AssertBoundary("wf_cnd_time_more_than", instant.AddMilliseconds(1500).ToOffset(TimeSpan.FromHours(-7)), true);

        void AssertBoundary(string name, DateTimeOffset now, bool expected)
        {
            var reads = 0;
            var condition = new WiredModernCondition(room, MakeItem(101, name), Descriptor(name), TestGroupManager.Empty,
                _ => null, () => { reads++; return now; });
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(condition, new() { IntParams = [2] }, out var configuration, out var error), error);
            condition.ApplyConfiguration(configuration);
            Assert.Equal(expected, condition.Execute(Context(room, new(WiredEventKind.Use), [], [])));
            Assert.Equal(1, reads);
        }
    }

    [Fact]
    public void AllImplementedEditorsHaveValidatedDefaults()
    {
        var (room, _, _) = World();

        foreach (var name in WiredTriggerConfiguration.Events.Keys) {
            Assert.True(WiredTriggerConfiguration.TryValidate(name, WiredTriggerConfiguration.Defaults(name), out _, out _), name);
        }

        foreach (var name in WiredConditionConfiguration.PositiveNames.Concat(WiredConditionConfiguration.NegativeNames.Keys)) {
            Assert.True(WiredConditionConfiguration.TryValidate(name, WiredConditionConfiguration.Defaults(name, 2040), out _, out _), name);
        }

        uint mappedItemId = 100;

        foreach (var name in WiredMovementActions.Names.Concat(WiredModernAction.OtherNames).Concat(WiredBotActions.Names)) {
            var box = ActionBox(room, name);
            var proposed = WiredActionConfiguration.Defaults(name);

            if (WiredNativeEditorProjection.Supports(name)) {
                var metadata = WiredNativeEditorProjection.Metadata(name);
                box.Item.Id = mappedItemId++;
                box.Item.RoomId = room.Id;
                Assert.True(room.GetRoomItemHandler().AdmitFloorItem(box.Item));
                var native = new WiredNativeEditorConfiguration
                {
                    Category = box.Descriptor.Category,
                    NativeCode = WiredNativeEditorProjection.Code(name),
                    OwnedIntParams = metadata.OwnedDefaults,
                    FurniSourceTypes = metadata.FurniDefaults,
                    UserSourceTypes = metadata.UserDefaults,
                    Delay = 0
                };
                Assert.Same(box.Item, room.GetRoomItemHandler().GetItem(box.Item.Id));
                Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, native, out proposed), name);
            }

            Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, proposed, out _, out _), name);
        }
    }

    [Fact]
    public void NegativeStackAndSplitSignalsCallRealOperationsWithSeparateRoles()
    {
        var (room, _, items) = World();
        var antenna = MakeItem(1, "antenna");
        var forwarded = MakeItem(2, "forwarded");
        items[1] = antenna;
        items[2] = forwarded;
        var clicked = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var operations = new RecordingOperations();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.ClickUser) { Actor = clicked, TargetUser = clicked },
            new(() => new[] { antenna, forwarded }, () => new[] { clicked }), operations);
        context.Triggering.UserIds.Add(clicked.VirtualId);
        var call = ActionBox(room, "wf_act_neg_call_stacks");
        call.Item.SetState(2, 2, 0, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(call, new() { IntParams = [100], SelectedItems = [1] }, out var callConfig, out _));
        call.ApplyConfiguration(callConfig);
        Assert.True(call.IsNegative);
        Assert.True(call.Execute(context));
        Assert.True(operations.CallNegative);
        Assert.Equal(new uint[] { 1 }, operations.Called);
        call.Item.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        Assert.False(call.Execute(context));
        Assert.Empty(operations.Called);
        var signal = ActionBox(room, "wf_act_neg_send_signal");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(signal, new() { IntParams = [1, 101, 0, 1, 1, 0], SelectedItems = [1], SecondarySelectedItems = [2] }, out var signalConfig, out _));
        signal.ApplyConfiguration(signalConfig);
        Assert.True(signal.Execute(context));
        var received = Assert.Single(operations.Signals);
        Assert.True(received.Negative);
        Assert.Equal(new uint[] { 1 }, received.Receivers);
        Assert.Equal(new uint[] { 2 }, received.Selection.FurniIds);
        Assert.Equal(new[] { 7 }, received.Selection.UserIds);
    }

    [Fact]
    public void ConfiguredClockActionControlsActualAttachedClock()
    {
        var (room, _, items) = World();
        var item = MakeItem(1, "wf_upcounter1");
        items[1] = item;
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        var box = ActionBox(room, "wf_act_adjust_clock", clocks);
        WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [2, 100, 1, 3], SelectedItems = [1] }, out var config, out _);
        box.ApplyConfiguration(config);
        Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [item], [])));
        Assert.Equal(61500, clocks.ReadMilliseconds(item));
        Assert.False(clocks.HasRunning);
        clocks.Control(item, 0, 0);
        Assert.True(clocks.HasRunning);
        clocks.Forget(item);
        Assert.False(clocks.HasRunning);
    }

    [Fact]
    public void LogActionWritesBoundedRoomMonitorAndEmptyTextHasNoEffect()
    {
        var (room, _, _) = World();
        var log = new WiredRoomLog(2);
        var box = ActionBox(room, "wf_act_neg_log", log: log);
        WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [1, 0], Text = "First" }, out var config, out _);
        box.ApplyConfiguration(config);
        Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
        Assert.Equal("First", Assert.Single(log.Read(0, 10).Entries).Message);
        log.Append(2, 100, "Second", DateTimeOffset.UtcNow);
        log.Append(1, 100, "Third", DateTimeOffset.UtcNow);
        Assert.Equal(2, log.Read(0, 10).Total);
        Assert.Equal("Third", Assert.Single(log.Read(0, 10, 1, "third").Entries).Message);
        WiredNativeTestSupport.InstallRuntime(box, config with { Text = "" });
        Assert.False(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));
    }

    [Fact]
    public void LogActionCapturesTheRequiredClockOnce()
    {
        var (room, _, _) = World();
        var instant = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new CountingClock(instant, TimeZoneInfo.Utc);
        var log = new WiredRoomLog();
        var box = ActionBox(room, "wf_act_log", log: log, clock: clock);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [1, 0], Text = "Captured" }, out var config, out _));
        box.ApplyConfiguration(config);

        Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [], [])));

        Assert.Equal(instant, Assert.Single(log.Read(0, 10).Entries).Timestamp);
        Assert.Equal(1, clock.Calls);
    }

    [Fact]
    public async Task FiredLogLineReachesTheLogPageAndMonitorForInspectorsOnly()
    {
        using var f = new TeleportFixture();
        f.Room.OwnerName = "Alice";
        f.Room.Type = "private";
        f.Room.UsersWithRights = [];
        var store = new MonitorSettingsStore();
        var settings = new WiredRoomSettings(f.Room, store);
        typeof(WiredComponent).GetField("<Settings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.Room.GetWired(), settings);
        var item = MakeItem(102, "wf_act_log");
        var box = Assert.IsType<WiredModernAction>(f.Room.GetWired().CreateConfiguredBox(item, Descriptor("wf_act_log")));
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [2, 0], Text = "Gate opened" }, out var config, out _));
        box.ApplyConfiguration(config);
        f.Items[102] = item;
        f.Engine.Add(box);
        f.Fire();

        var alice = Capture(f.Client);
        await RoomLogsPage().Parse(f.Room, f.Client, Request(1, 50, -1, -1, ""));
        var page = Reply(alice, ServerPacketHeader.WiredRoomLogPageComposer);
        Assert.Equal((1, 1, 50, 1), (page.Int(), page.Int(), page.Int(), page.Int()));
        Assert.Equal((1d, 2, 8, "Gate opened"), (page.Long(), page.Byte(), page.Byte(), page.String()));
        var millis = page.Long();
        Assert.InRange(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - millis, 0, 60_000);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds((long)millis).UtcDateTime.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture), page.String());
        Assert.Equal((false, false, false), (page.Bool(), page.Bool(), page.Bool()));
        page.End();

        await RoomLogsPage().Parse(f.Room, f.Client, Request(9, 50, 2, 8, " GATE "));
        page = Reply(alice, ServerPacketHeader.WiredRoomLogPageComposer);
        Assert.Equal((1, 1, 50, 1), (page.Int(), page.Int(), page.Int(), page.Int()));
        Assert.Equal("Gate opened", page.Skip(2, 1, 1).String());
        page.Skip(2).String();
        Assert.Equal((true, 2, true, 8, true, "GATE"), (page.Bool(), page.Byte(), page.Bool(), page.Byte(), page.Bool(), page.String()));
        page.End();
        await RoomLogsPage().Parse(f.Room, f.Client, Request(1, 50, -1, 4, "")); // a source Plus never writes
        page = Reply(alice, ServerPacketHeader.WiredRoomLogPageComposer);
        Assert.Equal((0, 1, 50, 0), (page.Int(), page.Int(), page.Int(), page.Int()));

        await MonitorRequest().Parse(f.Room, f.Client, Request(0));
        var monitor = Reply(alice, 5101);
        monitor.Skip(1);
        Assert.Equal(10000, monitor.Int());
        Assert.False(monitor.Bool());
        monitor.Skip(1);
        Assert.Equal(100, monitor.Int());
        monitor.Skip(3);
        Assert.Equal(20, monitor.Int());
        Assert.Equal((0, 1000, 0, 0, 0, 0, 0, 0), (monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int(), monitor.Int()));
        Assert.Equal(4, monitor.Int());
        var tallies = Enumerable.Range(0, 4).Select(_ => (Type: monitor.String(), Severity: monitor.String(), Count: monitor.Int(), Seconds: monitor.Int(),
            Reason: monitor.String(), Label: monitor.String(), Id: monitor.Int())).ToArray();
        Assert.Equal(["EXECUTION_CAP", "DELAYED_EVENTS_CAP", "RECURSION_TIMEOUT", "WIRED_LOG"], tallies.Select(x => x.Type));
        Assert.All(tallies[..3], tally => Assert.Equal(("ERROR", 0, 0), (tally.Severity, tally.Count, tally.Seconds)));
        Assert.Equal(("WARNING", 1, "Gate opened", "wf_act_log", 102), (tallies[3].Severity, tallies[3].Count, tallies[3].Reason, tallies[3].Label, tallies[3].Id));
        Assert.Equal(1, monitor.Int());
        Assert.Equal(("WIRED_LOG", "WARNING"), (monitor.String(), monitor.String()));
        Assert.Equal(tallies[3].Seconds, monitor.Int());
        Assert.Equal(("Gate opened", "wf_act_log", 102), (monitor.String(), monitor.String(), monitor.Int()));
        monitor.End();

        // Without inspect rights neither request answers; with inspect only, a clear is refused.
        var bob = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { Revision = f.Client.Revision };
        bob.SetHabbo(new Habbo { Id = 2, Username = "Bob", CurrentRoom = f.Room });
        var bobReplies = Capture(bob);
        await RoomLogsPage().Parse(f.Room, bob, Request(1, 50, -1, -1, ""));
        await MonitorRequest().Parse(f.Room, bob, Request(0));
        Assert.Empty(bobReplies);
        store.Saved = new(InspectMask: (int)WiredRoomAccess.Everyone);
        settings.Reload();
        await MonitorRequest().Parse(f.Room, bob, Request(1));
        Assert.Empty(bobReplies);
        var pages = RoomLogsPage();
        await pages.Parse(f.Room, bob, Request(1, 50, -1, -1, ""));
        await pages.Parse(f.Room, bob, Request(1, 50, -1, -1, "")); // inside the 250 ms page interval
        Assert.Equal(1, Reply(bobReplies, ServerPacketHeader.WiredRoomLogPageComposer).Int());
        Assert.Empty(bobReplies);

        await MonitorRequest().Parse(f.Room, f.Client, Request(1));
        monitor = Reply(alice, 5101).Skip(16, 1);
        Assert.Equal(4, monitor.Int());

        for (var i = 0; i < 4; i++) {
            monitor.String();
            monitor.String();
            Assert.Equal(0, monitor.Int());
            monitor.Skip(1).String();
            monitor.String();
            monitor.Int();
        }

        Assert.Equal(0, monitor.Int());
        monitor.End();
        await RoomLogsPage().Parse(f.Room, f.Client, Request(1, 50, -1, -1, "x", 1)); // trailing data is malformed
        Assert.Empty(alice);
    }

    [Fact]
    public async Task UnauthorizedMenuAndMonitorRequestsNeverReadStateAndWritesRefuseVisibly()
    {
        using var f = new TeleportFixture();
        f.Room.OwnerName = "Alice";
        f.Room.Type = "private";
        f.Room.UsersWithRights = [];
        var bob = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { Revision = f.Client.Revision };
        bob.SetHabbo(new Habbo { Id = 2, Username = "Bob", CurrentRoom = f.Room });
        var replies = Capture(bob);
        var menus = new WiredVariableMenuService();
        var monitor = new WiredMonitorService(new WiredRequestGateService(TimeProvider.System));
        // The fixture's database throws on any connection, so every denied request must return before a lazy read.
        await new WiredUserVariablesRequestEvent(menus).Parse(f.Room, bob, Request());
        await new WiredAllVariablesRequestEvent(menus).Parse(f.Room, bob, Request());
        await new WiredVariableHashesEvent(menus).Parse(f.Room, bob, Request(0));
        await new WiredVariableHoldersRequestEvent(menus).Parse(f.Room, bob, Request("user:10"));
        await new WiredVariableHoldersPageEvent(menus).Parse(f.Room, bob, Request("user:10", 1, 15, 0, -1));
        await new WiredUserVariableUpdateEvent(menus).Parse(f.Room, bob, Request(3, (int)f.Room.Id, 12, 9));
        await new WiredUserVariableManageEvent(menus).Parse(f.Room, bob, Request(2, 0, 2, 12, 0));
        await new WiredMonitorRequestEvent(monitor).Parse(f.Room, bob, Request(0));
        await new WiredRoomLogsPageEvent(monitor).Parse(f.Room, bob, Request(1, 50, -1, -1, ""));
        Assert.Equal(2, replies.Count);
        Assert.All(replies, reply => Assert.Equal(ServerPacketHeader.BroadcastMessageAlertComposer, reply.Header));
    }

    [Fact]
    public async Task DeniedMonitorClearLeavesLogsAndItsGateForTheSameSessionOnceGranted()
    {
        using var f = new TeleportFixture();
        f.Room.OwnerName = "Alice";
        f.Room.Type = "private";
        f.Room.UsersWithRights = [];
        var store = new MonitorSettingsStore();
        var settings = new WiredRoomSettings(f.Room, store);
        typeof(WiredComponent).GetField("<Settings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(f.Room.GetWired(), settings);
        store.Saved = new(InspectMask: (int)WiredRoomAccess.Everyone);
        settings.Reload();

        // Seed one room log line through the real wired log action.
        var item = MakeItem(102, "wf_act_log");
        var box = Assert.IsType<WiredModernAction>(f.Room.GetWired().CreateConfiguredBox(item, Descriptor("wf_act_log")));
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [2, 0], Text = "Gate opened" }, out var config, out _));
        box.ApplyConfiguration(config);
        f.Items[102] = item;
        f.Engine.Add(box);
        f.Fire();
        Assert.Equal(1, f.Room.GetWired().ReadLogs(0, 50, -1, "", -1).Total);

        // Bob can inspect but not manage, so his clear is denied and neither clears the log nor uses his clear gate.
        var bobHabbo = new Habbo { Id = 2, Username = "Bob", CurrentRoom = f.Room, Access = EditorTestSupport.Access([]) };
        var bob = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient) { Revision = f.Client.Revision };
        bob.SetHabbo(bobHabbo);
        var bobReplies = Capture(bob);
        var clear = new WiredMonitorRequestEvent(new WiredMonitorService(new WiredRequestGateService(new ManualMonotonicClock())));

        await clear.Parse(f.Room, bob, Request(1));
        Assert.Empty(bobReplies);
        Assert.Equal(1, f.Room.GetWired().ReadLogs(0, 50, -1, "", -1).Total);

        // The same session is then granted manage rights through its access seam and clears at the unchanged instant.
        bobHabbo.Access = EditorTestSupport.Access([Plus.HabboHotel.Permissions.PermissionKeys.RoomOwnerAny]);
        await clear.Parse(f.Room, bob, Request(1));
        Assert.Single(bobReplies);
        Assert.Equal(5101u, bobReplies[0].Header);
        Assert.Equal(0, f.Room.GetWired().ReadLogs(0, 50, -1, "", -1).Total);
    }

    [Fact]
    public async Task MonitorAndLogGatesUseTheInjectedMonotonicClockAndStayIndependent()
    {
        using var f = new TeleportFixture();
        f.Room.OwnerName = "Alice";
        f.Room.Type = "private";
        f.Room.UsersWithRights = [];
        var clock = new ManualMonotonicClock();
        var monitor = new WiredMonitorService(new WiredRequestGateService(clock));
        var fetch = new WiredMonitorRequestEvent(monitor);
        var pages = new WiredRoomLogsPageEvent(monitor);
        var alice = Capture(f.Client);
        int Monitors() => alice.Count(reply => reply.Header == 5101);
        int Pages() => alice.Count(reply => reply.Header == ServerPacketHeader.WiredRoomLogPageComposer);

        await fetch.Parse(f.Room, f.Client, Request(0));
        Assert.Equal(1, Monitors()); // the first request passes
        await fetch.Parse(f.Room, f.Client, Request(0));
        Assert.Equal(1, Monitors()); // a repeat at the same instant is refused
        clock.Advance(199);
        await fetch.Parse(f.Room, f.Client, Request(0));
        Assert.Equal(1, Monitors());
        clock.Advance(1);
        await fetch.Parse(f.Room, f.Client, Request(0));
        Assert.Equal(2, Monitors()); // exactly 200 ms passes

        await fetch.Parse(f.Room, f.Client, Request(1));
        Assert.Equal(3, Monitors()); // a clear has its own gate
        await fetch.Parse(f.Room, f.Client, Request(1));
        Assert.Equal(3, Monitors());

        await pages.Parse(f.Room, f.Client, Request(1, 50, -1, -1, ""));
        Assert.Equal(1, Pages());
        clock.Advance(249);
        await pages.Parse(f.Room, f.Client, Request(1, 50, -1, -1, ""));
        Assert.Equal(1, Pages());
        clock.Advance(1);
        await pages.Parse(f.Room, f.Client, Request(0, 50, -1, -1, ""));
        Assert.Equal(2, Pages()); // page 0 is normalized to page 1

        // Malformed requests return before the gate, so they cannot use up the interval.
        clock.Advance(250);
        await pages.Parse(f.Room, f.Client, Request(1, 50, -1, -1, "x", 1));
        Assert.Equal(2, Pages());
        await pages.Parse(f.Room, f.Client, Request(1, 50, -1, -1, ""));
        Assert.Equal(3, Pages());
    }

    private sealed class ManualMonotonicClock : TimeProvider
    {
        private long _now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _now;
        public void Advance(long milliseconds) => _now += milliseconds;
    }

    // Each request source gets its own rate gates, as in production, unless a test shares one explicitly.
    private static WiredRoomLogsPageEvent RoomLogsPage() => new(new WiredMonitorService(new WiredRequestGateService(TimeProvider.System)));
    private static WiredMonitorRequestEvent MonitorRequest() => new(new WiredMonitorService(new WiredRequestGateService(TimeProvider.System)));

    private static List<(uint Header, byte[] Body)> Capture(GameClient client)
    {
        var replies = new List<(uint, byte[])>();
        client.SendCallback = args => { replies.Add(((uint)FlashGameClient.DecodeInt16(args.MemoryBuffer.Slice(4, 2)), args.MemoryBuffer[6..].ToArray())); return true; };

        return replies;
    }

    private static WireReader Reply(List<(uint Header, byte[] Body)> replies, uint header)
    {
        var reply = replies[0];
        replies.RemoveAt(0);
        Assert.Equal(header, reply.Header);

        return new(reply.Body);
    }

    private static FlashIncomingPacket Request(params object[] values)
    {
        using var stream = new MemoryStream();

        foreach (var value in values) {
            if (value is string text) {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var length = new byte[2];
                System.Buffers.Binary.BinaryPrimitives.WriteInt16BigEndian(length, (short)bytes.Length);
                stream.Write(length);
                stream.Write(bytes);
            }
            else {
                var bytes = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, (int)value);
                stream.Write(bytes);
            }
        }

        return new FlashIncomingPacket { Buffer = stream.ToArray() };
    }

    /// <summary>Reads a reply the way the client's parsers do, down to its last byte.</summary>
    private sealed class WireReader(byte[] body)
    {
        private int _position;
        public int Int()
        {
            var value = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(_position));
            _position += 4;

            return value;
        }
        public int Byte() => body[_position++];
        public bool Bool()
        {
            var value = Byte();
            Assert.InRange(value, 0, 1);

            return value == 1;
        }
        public string String()
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt16BigEndian(body.AsSpan(_position));
            _position += 2;
            var text = System.Text.Encoding.UTF8.GetString(body, _position, length);
            _position += length;

            return text;
        }
        // The client's readWiredLong: an unsigned high and low half.
        public double Long() => (uint)Int() * 4294967296d + (uint)Int();
        public WireReader Skip(int ints, int bytes = 0, int moreBytes = 0)
        {
            _position += ints * 4 + bytes + moreBytes;

            return this;
        }
        public void End() => Assert.Equal(body.Length, _position);
    }

    private sealed class MonitorSettingsStore : IWiredRoomSettingsStore
    {
        public WiredRoomSettingsSnapshot? Saved = new();
        public WiredRoomSettingsSnapshot? Load(uint roomId) => Saved;
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) =>
            throw new NotSupportedException();
    }





    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CanonicalMovementUnequalFieldsFollowNativeGrammar(int type)
    {
        var bytes = TypedFurnitureStateTests.Payload(new WiredMovementComposer(type, 19, 0, 1, 1.25, 2, 3, 3.5,
            type == 0 ? 2 : 4, 6, 750));
        using var reader = new BinaryReader(new MemoryStream(bytes));
        int Int() => TypedFurnitureStateTests.ReadInt(reader);
        Assert.Equal(1, Int());
        Assert.Equal(type, Int());
        Assert.Equal(0, Int());
        Assert.Equal(1, Int());
        Assert.Equal(2, Int());
        Assert.Equal(3, Int());
        Assert.Equal("1.25", TypedFurnitureStateTests.ReadString(reader));
        Assert.Equal("3.5", TypedFurnitureStateTests.ReadString(reader));
        Assert.Equal(19, Int());

        if (type == 0) {
            Assert.Equal(1, Int());
            Assert.Equal(750, Int());
            Assert.Equal(2, Int());
            Assert.Equal(6, Int());
            Assert.False(reader.ReadBoolean());
        }
        else {
            Assert.Equal(750, Int());
            Assert.Equal(4, Int());
            Assert.False(reader.ReadBoolean());
            Assert.False(reader.ReadBoolean());
        }

        Assert.Equal(bytes.Length, reader.BaseStream.Position);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, 0)]
    [InlineData(0, 100)]
    [InlineData(0, -100)]
    [InlineData(1, null)]
    [InlineData(1, 0)]
    [InlineData(1, 100)]
    [InlineData(1, -100)]
    public void CanonicalMovementOptionalFieldsPreservePresenceSignedValuesAndBatchBody(int type, int? option)
    {
        var movement = new WiredMovementComposer(type, 19, 0, 1, 1.25, 2, 3, 3.5, 2, 6, 750)
        {
            AnimationType = 0,
            JumpPower = option,
            OvershootTimeMs = option,
            CurveStrength = option
        };
        var single = TypedFurnitureStateTests.Payload(movement);
        var batch = TypedFurnitureStateTests.Payload(new WiredMovementBatchComposer([movement]));
        Assert.Equal(single, batch);
        var reader = new WireReader(single);
        Assert.Equal(1, reader.Int());
        Assert.Equal(type, reader.Int());
        reader.Skip(4);
        Assert.Equal("1.25", reader.String());
        Assert.Equal("3.5", reader.String());
        Assert.Equal(19, reader.Int());

        if (type == 0) {
            Assert.Equal(0, reader.Int());
        }

        Assert.Equal(750, reader.Int());
        Assert.Equal(2, reader.Int());

        if (type == 0) {
            Assert.Equal(6, reader.Int());
        }

        Assert.Equal(option.HasValue, reader.Bool());

        if (option is { } value) {
            Assert.Equal(value, reader.Int());
        }

        if (type == 1) {
            Assert.Equal(option.HasValue, reader.Bool());

            if (option is { } curve) {
                Assert.Equal(curve, reader.Int());
            }
        }

        reader.End();
    }

    [Fact]
    public void CanonicalMovementHeterogeneousBatchContainsExactlyTheSingleEntryBodies()
    {
        WiredMovementComposer[] entries = [
            new(1, 11, 0, 1, 1, 2, 3, 2, 4, 6, 750) { OvershootTimeMs = 0, CurveStrength = -100 },
            new(0, 19, 2, 3, 2, 4, 5, 3, 2, 6, 200) { JumpPower = 100 },
            new(1, 12, 4, 5, 3, 6, 7, 4, 1, 7, 500)
        ];
        var batch = TypedFurnitureStateTests.Payload(new WiredMovementBatchComposer(entries));
        using var reader = new BinaryReader(new MemoryStream(batch));
        Assert.Equal(entries.Length, TypedFurnitureStateTests.ReadInt(reader));

        foreach (var entry in entries) {
            var expected = TypedFurnitureStateTests.Payload(entry)[4..];
            Assert.Equal(expected, reader.ReadBytes(expected.Length));
        }

        Assert.Equal(batch.Length, reader.BaseStream.Position);
    }

    [Fact]
    public void ActiveChatAndMovementComposersPreserveParserFields()
    {
        var fields = new List<object>();
        var packet = DispatchProxy.Create<IOutgoingPacket, RecordingProxy>();
        ((RecordingProxy)(object)packet).InvokeMethod = (_, args) => { fields.Add(args![0]!); return null; };
        new WiredChatComposer(7, "Hello", 252, 2, true).Compose(packet);
        Assert.Equal(new object[] { 7, "Hello", 0, 252, 0, 5, 2 }, fields);
        fields.Clear();
        new WiredMovementComposer(1, 10, 0, 1, 1.25, 2, 2, 3.5, 4, 4, 750).Compose(packet);
        Assert.Equal(new object[] { 1, 1, 0, 1, 2, 2, "1.25", "3.5", 10, 750, 4, false, false }, fields);
        fields.Clear();
        new WiredRewardResultComposer(5).Compose(packet);
        Assert.Equal(new object[] { 5 }, fields);
        Assert.Equal(ServerPacketHeader.WiredRewardResultComposer, new WiredRewardResultComposer(5).MessageId);
        var (room, _, _) = World();
        var box = ActionBox(room, "wf_act_show_message");
        LoadStoredRuntime(box, "wf_act_show_message", new() { IntParams = [0, 0, 252, 2], Text = "Hello" });
        var installed = box.Configuration;
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, installed, out var config, out _));
        Assert.Equal(2, config.IntParams[3]);
        Assert.False(WiredNativeTestSupport.TryCompileRuntime(box,
            new() { IntParams = [0, 0, 252, 3], Text = "Hello" }, out _));
        Assert.Same(installed, box.Configuration);
    }

    private sealed class RecordingOperations : IWiredRuntimeOperations
    {
        public uint[] Called = []; public bool CallNegative;
        public List<(uint[] Receivers, WiredSelection Selection, bool Negative)> Signals = [];
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false)
        {
            Called = targets.Select(x => x.Id).ToArray();
            CallNegative = negative;

            return Called.Length > 0;
        }
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false)
        {
            Signals.Add((receivers.Select(x => x.Id).ToArray(), selection.Copy(), negative));

            return true;
        }
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    [Fact]
    public void TemporaryEffectLeasesPreserveNewEffectsOverlapsAndRoomVisitIdentity()
    {
        var (room, _, _) = World();
        var user = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var effects = new WiredTemporaryEffects();
        var current = 8;
        var attached = true;
        var first = effects.Acquire(user, () => current, value => current = value, () => attached);
        var second = effects.Acquire(user, () => current, value => current = value, () => attached);
        first();
        Assert.Equal(4, current);
        second();
        Assert.Equal(8, current);
        second();
        Assert.Equal(8, current);
        var changed = effects.Acquire(user, () => current, value => current = value, () => attached);
        current = 12;
        changed();
        Assert.Equal(12, current);
        var departed = effects.Acquire(user, () => current, value => current = value, () => attached);
        attached = false;
        current = -1;
        departed();
        Assert.Equal(-1, current);
        attached = true;
        var capFailure = effects.Acquire(user, () => current, value => current = value, () => attached);
        capFailure();
        Assert.Equal(-1, current); // Immediate cleanup when engine refuses the restore callback.
    }

    [Fact]
    public void TemporaryEffectsLifecycleReleasesOnlyAttachedUnchangedVisits()
    {
        var (room, _, _) = World();
        var user = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var effects = new WiredTemporaryEffects();
        var current = 8;
        var attached = true;
        var restore = effects.Acquire(user, () => current, value => current = value, () => attached);
        effects.Forget(user);
        Assert.Equal(8, current);
        current = 12;
        restore();
        Assert.Equal(12, current);
        effects.Acquire(user, () => current, value => current = value, () => attached);
        effects.Clear();
        Assert.Equal(12, current);
        effects.Acquire(user, () => current, value => current = value, () => attached);
        attached = false;
        current = -1;
        effects.Clear();
        Assert.Equal(-1, current);
    }

    [Fact]
    public void HeadingRemembersTurnAndStopDoesNotInventMovement()
    {
        var item = MakeItem(1, "test");
        var directions = new WiredDirectionalActions();
        var attempts = new List<Point>();
        Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return x == 1; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new[] { new Point(0, -1), new Point(1, 0) }, attempts);
        attempts.Clear();
        Assert.True(directions.MoveHeading(item, 0, 1, false, (x, y) => { attempts.Add(new(x, y)); return true; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Equal(new Point(1, 0), Assert.Single(attempts));
        attempts.Clear();
        Assert.False(new WiredDirectionalActions().MoveHeading(item, 0, 6, false,
            (x, y) => { attempts.Add(new(x, y)); return false; }, (_, _) => [], (_, _) => throw new Exception()));
        Assert.Single(attempts);
        Assert.Equal(2, WiredDirectionalActions.AvatarRotation(0, 8));
        Assert.Equal(6, WiredDirectionalActions.AvatarRotation(0, 9));
    }

    [Theory]
    [InlineData(0, 1, 1)] // Wait.
    [InlineData(1, 2, 0)] // Right 45.
    [InlineData(2, 2, 1)] // Right 90.
    [InlineData(3, 0, 0)] // Left 45.
    [InlineData(4, 0, 1)] // Left 90.
    [InlineData(5, 1, 2)] // Turn back.
    public async Task MoveToDirectionExecutesCurrentEditorTurnChoices(int choice, int x, int y)
    {
        var f = new MovementOracleSession("wf_act_move_to_dir");
        var item = f.Items[8];
        f.Map.Model.SqState[1, 0] = SquareState.Blocked;
        await f.Save([0, choice, 0], [8]);
        var action = f.Action;
        var editor = f.Open();
        AssertMovementOracleReply(editor, true, [0, choice, 0], [8], 0);
        var context = Context(f.Room, new(WiredEventKind.Use), [item], []);
        context.Policy.Addons.DisableAnimation = true;

        Assert.Equal(choice != 0, action.Execute(context));

        Assert.Equal(new Point(x, y), item.Coordinate);
        Assert.Equal(0, item.Rotation);
        var before = action.Configuration;
        var writes = f.Store.Saves.Count;
        var published = f.Published;
        var pending = f.SeedPending();
        f.Client.Packets.Clear();
        await f.Save(editor.Owned, editor.Primary, editor.Delay);
        f.AssertExplicitSave(before, writes, published, pending);
        Assert.Equal(new[] { 0, choice, 100, 0 }, action.Configuration.IntParams.ToArray());
    }

    [Fact]
    public async Task MoveToDirectionRandomRetriesEightBlockedAttempts()
    {
        var f = new MovementOracleSession("wf_act_move_to_dir");
        var item = f.Items[8];
        var users = new List<RoomUser>();

        for (var direction = 0; direction < 8; direction++) {
            var offset = WiredRoomOperations.Offset(direction);
            var user = new RoomUser(direction + 1, f.Room.Id, direction + 20, f.Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
            user.SetPos(1 + offset.X, 1 + offset.Y, 0);
            RoomUsers(f.Room)[user.VirtualId] = user;
            f.Map.AddUserToMap(user, user.Coordinate);
            users.Add(user);
        }

        await f.Save([0, 6, 1], [8]);
        AssertMovementOracleReply(f.Open(), true, [0, 6, 1], [8], 0);
        var collisions = 0;
        typeof(WiredModernAction).GetField("_publish", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(f.Action,
            (Action<WiredRuntimeEvent>)(e =>
            {
                if (e.Kind == WiredEventKind.Collision) {
                    collisions++;
                }
            }));

        Assert.False(f.Action.Execute(Context(f.Room, new(WiredEventKind.Use), [item], users.ToArray())));

        Assert.Equal(8, collisions);
        Assert.Equal(new Point(1, 1), item.Coordinate);
    }

    [Theory]
    [InlineData("wf_act_move_to_dir", true)]
    [InlineData("wf_act_move_to_dir", false)]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    public void LineBlockedByItselfWaitsBehindItsFrontAtAWall(string name, bool frontFirst)
    {
        var (room, line) = TileLine(0, -1, 1, frontFirst);
        var action = LineBox(room, name, 6, line);

        for (var pulse = 0; pulse < 3; pulse++) {
            Assert.False(Pulse(room, action, line, blockedBySelf: true));
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, line.Select(item => item.GetX));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GappedLineBlockedByItselfClosesUpBehindItsWaitingFrontWithoutStacking(bool frontFirst)
    {
        var (room, line) = TileLine(0, -1, 2, frontFirst);
        var action = LineBox(room, "wf_act_move_to_dir", 6, line);

        Assert.True(Pulse(room, action, line, blockedBySelf: true));
        Assert.Equal(new[] { 0, 1, 3, 5, 7, 9, 11 }, line.Select(item => item.GetX));

        for (var pulse = 0; pulse < 6; pulse++) {
            Pulse(room, action, line, blockedBySelf: true);
        }

        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6 }, line.Select(item => item.GetX));
    }

    [Theory]
    [InlineData("wf_act_move_to_dir", 1, true)]
    [InlineData("wf_act_move_to_dir", 1, false)]
    [InlineData("wf_act_move_to_dir", 2, true)]
    [InlineData("wf_act_move_to_dir", 2, false)]
    [InlineData("wf_act_move_rotate", 1, true)]
    [InlineData("wf_act_move_rotate", 1, false)]
    public void StepOrderDeterminesWhetherALineKeepsItsSpacingInFreeSpace(string name, int spacing, bool frontFirst)
    {
        var (room, line) = TileLine(6 * spacing, 1, spacing, frontFirst);
        var action = LineBox(room, name, 2, line);

        for (var pulse = 1; pulse <= 2; pulse++) {
            Assert.True(Pulse(room, action, line, blockedBySelf: true));
            Assert.Equal(Enumerable.Range(0, line.Length).Select(i => 6 * spacing - spacing * i
                + (name == "wf_act_move_to_dir" && spacing == 1 && !frontFirst ? Math.Max(0, pulse - i) : pulse)), line.Select(item => item.GetX));
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DirectionalItemsTurnAtAWallInTheirOwnExecutionOrder(bool frontFirst)
    {
        var (room, line) = TileLine(0, -1, 1, frontFirst);
        var action = LineBox(room, "wf_act_move_to_dir", 6, line, turn: 5);

        Assert.True(Pulse(room, action, line, blockedBySelf: true));

        Assert.Equal(frontFirst ? new[] { 0, 1, 2, 3, 4, 5, 7 } : [1, 2, 3, 4, 5, 6, 7], line.Select(item => item.GetX));
    }

    [Theory]
    [InlineData("wf_act_move_to_dir", true)]
    [InlineData("wf_act_move_to_dir", false)]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    public void NormalDirectionalPlacementStacksWhileGroupedStepsWaitAtAWall(string name, bool frontFirst)
    {
        var (room, line) = TileLine(0, -1, 1, frontFirst);
        var action = LineBox(room, name, 6, line);

        for (var pulse = 0; pulse < 3; pulse++) {
            Assert.Equal(name == "wf_act_move_to_dir", Pulse(room, action, line, blockedBySelf: false));
        }

        Assert.Equal(name == "wf_act_move_to_dir" ? new[] { 0, 0, 0, 0, 1, 2, 3 } : [0, 1, 2, 3, 4, 5, 6], line.Select(item => item.GetX));

        var (open, free) = TileLine(6, 1, 1, frontFirst);
        var east = LineBox(open, name, 2, free);

        Assert.True(Pulse(open, east, free, blockedBySelf: false));
        Assert.Equal(new[] { 7, 6, 5, 4, 3, 2, 1 }, free.Select(item => item.GetX));
    }

    [Theory]
    [InlineData(InteractionType.None, true, false, false)]
    [InlineData(InteractionType.None, true, true, true)]
    [InlineData(InteractionType.None, false, false, false)]
    [InlineData(InteractionType.None, false, true, true)]
    [InlineData(InteractionType.Stacktool, true, false, false)]
    [InlineData(InteractionType.Stacktool, true, true, true)]
    public void StepIsBlockedByAnyFurnitureUnlessPhysicsMovesThroughIt(InteractionType type, bool stackable, bool through, bool moves)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var mover = MakeItem(1, "test");
        mover.SetState(0, 1, 0, Gamemap.GetAffectedTiles(1, 1, 0, 1, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var obstacle = MakeItem(2, "obstacle");
        obstacle.Definition.InteractionType = type;
        obstacle.Definition.Stackable = stackable;
        obstacle.Definition.Height = 0.5;
        obstacle.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        items[2] = obstacle;
        map.AddToMap(obstacle);
        var action = ActionBox(room, "wf_act_move_rotate");
        LoadStoredRuntime(action, "wf_act_move_rotate", new() { IntParams = [2, 0, 100, 0], SelectedItems = [1] });
        var context = Context(room, new(WiredEventKind.Use), [mover, obstacle], []);
        context.Policy.Addons.DisableAnimation = true;
        var throughFurni = through ? new HashSet<uint> { 2 } : new HashSet<uint>();

        if (through) {
            context.Policy.Addons.Physics = new(false, throughFurni, new HashSet<int>(), new HashSet<uint>());
        }

        Assert.Equal(moves, action.Execute(context));
        Assert.Equal(moves ? new Point(1, 1) : new Point(0, 1), mover.Coordinate);
        Assert.Equal(moves ? type == InteractionType.Stacktool ? 0 : 0.5 : 0, mover.GetZ);
        Assert.Equal(Plus.HabboHotel.Items.Wired.Modern.Addons.WiredMovementPolicy.IsBlocked(context.Policy.Addons.Physics, [2], [], true, false),
            new WiredCollisionPolicy(throughFurni, new HashSet<int>(), new HashSet<uint>(), Step: true).BlocksFurni(obstacle));
    }

    [Fact]
    public void ValidMovesCheckUsesTheStepRuleSoAStackableNeighbourBlocksUnlessMovedThrough()
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var mover = MakeItem(1, "test");
        mover.SetState(0, 1, 0, Gamemap.GetAffectedTiles(1, 1, 0, 1, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var tile = MakeItem(2, "color_tile");
        tile.Definition.Stackable = true;
        tile.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        items[2] = tile;
        map.AddToMap(tile);
        var through = new Plus.HabboHotel.Items.Wired.Modern.Addons.WiredPhysicsPolicy(false, new HashSet<uint> { 2 }, new HashSet<int>(), new HashSet<uint>());

        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: WiredRoomMovement.Collision(null, step: true)));
        Assert.True(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: WiredRoomMovement.Collision(through, step: true)));
    }

    [Theory]
    [InlineData("wf_act_rel_mov", new[] { 1, 1, 1, 0, 100 }, false)]
    [InlineData("wf_act_move_rotate", new[] { -1, 2, 100, 0 }, true)]
    public void StepActionsStopAtStackableFurnitureButRotatingInPlaceIsNoStep(string name, int[] ints, bool changes)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var below = MakeItem(2, "color_tile");
        below.Definition.Stackable = true;
        below.SetState(0, 1, 0, Gamemap.GetAffectedTiles(1, 1, 0, 1, 0));
        items[2] = below;
        map.AddToMap(below);
        var ahead = MakeItem(3, "color_tile");
        ahead.Definition.Stackable = true;
        ahead.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        items[3] = ahead;
        map.AddToMap(ahead);
        var mover = MakeItem(1, "test");
        mover.SetState(0, 1, 0, Gamemap.GetAffectedTiles(1, 1, 0, 1, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var action = ActionBox(room, name);
        var proposed = new WiredConfiguration { IntParams = [.. ints], SelectedItems = [1] };

        if (name == "wf_act_move_furni_as_group") {
            var offset = WiredRoomOperations.Offset(ints[0]);
            proposed = proposed with { IntParams = [0, offset.X, offset.Y, 100, 101, 0], SecondarySelectedItems = [mover.Id] };
            LoadStoredDirectionalGroup(action, proposed);
        }
        else if (name is "wf_act_move_rotate" or "wf_act_move_to_dir") {
            LoadStoredRuntime(action, name, proposed);
        }
        else {
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, proposed, out var config, out var error), error);
            action.ApplyConfiguration(config);
        }

        var context = Context(room, new(WiredEventKind.Use), [mover, below, ahead], []);
        context.Policy.Addons.DisableAnimation = true;

        Assert.Equal(changes, action.Execute(context));
        Assert.Equal((0, 1, changes ? 2 : 0), (mover.GetX, mover.GetY, mover.Rotation));
    }

    [Fact]
    public void BoxSavedMidFlashFlashesAgainOnItsNextEvent()
    {
        var (room, _, _) = World();
        var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        var box = MakeItem(100, "wf_act_move_to_dir");
        box.LegacyDataString = "1";

        wired.OnEvent(box);

        Assert.True(box.UpdateNeeded);
        Assert.Equal(2, box.UpdateCounter);
    }

    [Fact]
    public void FurniToFurniAndManualPlacementStillStackOntoStackableFurniture()
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var mover = MakeItem(1, "test");
        mover.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var target = MakeItem(2, "color_tile");
        target.Definition.Stackable = true;
        target.Definition.Height = 0.5;
        target.SetState(2, 2, 0, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
        items[2] = target;
        map.AddToMap(target);
        var action = ActionBox(room, "wf_act_furni_to_furni");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new() { IntParams = [100, 100], Text = "2", SelectedItems = [1] }, out var config, out var error), error);
        action.ApplyConfiguration(config);

        Assert.True(action.Execute(Context(room, new(WiredEventKind.Use), [mover, target], [])));
        Assert.Equal((2, 2, 0.5), (mover.GetX, mover.GetY, mover.GetZ));

        Assert.True(map.ResolvePlacement(2, 2, mover.Id).CanStack);
        Assert.True(room.GetRoomItemHandler().SetFloorItem(null!, mover, 0, 0, 0, false, false, false));
        Assert.True(room.GetRoomItemHandler().SetFloorItem(null!, mover, 2, 2, 0, false, false, false));
        Assert.Equal((2, 2, 0.5), (mover.GetX, mover.GetY, mover.GetZ));
    }

    [Theory]
    [InlineData("wf_act_rel_mov", true)]
    [InlineData("wf_act_rel_mov", false)]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    [InlineData("wf_act_move_to_dir", true)]
    [InlineData("wf_act_move_to_dir", false)]
    public void StepActionsPreserveGroupsWhileDirectionalMovesUseIndependentItems(string name, bool tileFirst)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var stack = TileWithChair(map, items, 0, tileFirst);
        var action = StackBox(room, name, 2, stack);

        Assert.True(StackPulse(room, action, stack));

        Assert.Equal((name == "wf_act_move_to_dir" && !tileFirst ? 0 : 1, 1, 0.0), (stack[0].GetX, stack[0].GetY, stack[0].GetZ));
        Assert.Equal((1, 1, name == "wf_act_move_to_dir" && !tileFirst ? 0.0 : 0.5), (stack[1].GetX, stack[1].GetY, stack[1].GetZ));
    }

    [Theory]
    [InlineData("wf_act_rel_mov", true)]
    [InlineData("wf_act_rel_mov", false)]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    [InlineData("wf_act_move_to_dir", true)]
    [InlineData("wf_act_move_to_dir", false)]
    public void GroupedStepsBlockOnStackableTilesWhileDirectionalItemsUseNormalPlacement(string name, bool tileFirst)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var stack = TileWithChair(map, items, 0, tileFirst);
        var ahead = StackItem(map, items, 9, "color_tile", 1, 0, 0.5, true);
        var action = StackBox(room, name, 2, stack);

        var directional = name == "wf_act_move_to_dir";
        Assert.Equal(directional, StackPulse(room, action, stack, ahead));

        Assert.Equal((directional && tileFirst ? 1 : 0, 1, directional && tileFirst ? 0.5 : 0.0), (stack[0].GetX, stack[0].GetY, stack[0].GetZ));
        Assert.Equal((directional ? 1 : 0, 1, directional && tileFirst ? 1.0 : 0.5), (stack[1].GetX, stack[1].GetY, stack[1].GetZ));
    }

    [Theory]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    [InlineData("wf_act_rel_mov", true)]
    [InlineData("wf_act_rel_mov", false)]
    public void LineOfStacksWaitsIntactAtAWallAndKeepsItsSpacingInFreeSpace(string name, bool frontFirst)
    {
        var (room, line) = StackLine(0, -1, frontFirst);
        var action = StackBox(room, name, 6, line);

        for (var pulse = 0; pulse < 3; pulse++) {
            Assert.False(StackPulse(room, action, line));
        }

        AssertStackLine(line, [0, 1, 2, 3]);

        var (open, free) = StackLine(6, 1, frontFirst);
        var east = StackBox(open, name, 2, free);

        for (var pulse = 1; pulse <= 2; pulse++) {
            Assert.True(StackPulse(open, east, free));
            AssertStackLine(free, [6 + pulse, 5 + pulse, 4 + pulse, 3 + pulse]);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StackedDirectionalItemsKeepTheirOwnHeadingAfterBlockedAttempts(bool tileFirst)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var stack = TileWithChair(map, items, 1, tileFirst);
        var action = StackBox(room, "wf_act_move_to_dir", 2, stack, turn: 5);

        var first = tileFirst ? stack[0] : stack[1];
        var second = tileFirst ? stack[1] : stack[0];
        var positions = tileFirst ? new[] { (2, 2), (1, 1), (0, 0) } : [(2, 0), (1, 0), (0, 1)];

        for (var pulse = 0; pulse < positions.Length; pulse++) {
            Assert.True(StackPulse(room, action, stack));
            Assert.Equal((positions[pulse].Item1, 1, !tileFirst && pulse == 2 ? 0.5 : 0.0), (first.GetX, first.GetY, first.GetZ));
            Assert.Equal((positions[pulse].Item2, 1, tileFirst ? 0.5 : 0.0), (second.GetX, second.GetY, second.GetZ));
        }
    }

    [Theory]
    [InlineData("wf_act_rel_mov", true)]
    [InlineData("wf_act_rel_mov", false)]
    [InlineData("wf_act_move_rotate", true)]
    [InlineData("wf_act_move_rotate", false)]
    public void StackMovesOntoFurnitureItMovesThroughKeepingItsOffsets(string name, bool tileFirst)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var stack = TileWithChair(map, items, 0, tileFirst);
        var obstacle = StackItem(map, items, 9, "color_tile", 1, 0, 0.5, true);
        var action = StackBox(room, name, 2, stack);
        var physics = new Plus.HabboHotel.Items.Wired.Modern.Addons.WiredPhysicsPolicy(false, new HashSet<uint> { obstacle.Id },
            new HashSet<int>(), new HashSet<uint>());

        Assert.True(StackPulse(room, action, stack, obstacle, physics));

        Assert.Equal((1, 1, 0.5), (stack[0].GetX, stack[0].GetY, stack[0].GetZ));
        Assert.Equal((1, 1, 1.0), (stack[1].GetX, stack[1].GetY, stack[1].GetZ));
        Assert.Equal((1, 1, 0.0), (obstacle.GetX, obstacle.GetY, obstacle.GetZ));
    }

    // A stackable color tile with a chair on it at (x, 1), as [tile, chair]; the ids decide which one runs first.
    private static Item[] TileWithChair(Gamemap map, ConcurrentDictionary<uint, Item> items, int x, bool tileFirst, uint firstId = 1) =>
    [
        StackItem(map, items, tileFirst ? firstId : firstId + 1, "color_tile", x, 0, 0.5, true),
        StackItem(map, items, tileFirst ? firstId + 1 : firstId, "chair", x, 0.5, 1, false)
    ];

    private static Item StackItem(Gamemap map, ConcurrentDictionary<uint, Item> items, uint id, string name, int x, double z, double height, bool stackable)
    {
        var item = MakeItem(id, name);
        item.Definition.Stackable = stackable;
        item.Definition.Height = height;
        item.SetState(x, 1, z, Gamemap.GetAffectedTiles(1, 1, x, 1, 0));
        items[id] = item;
        map.AddToMap(item);

        return item;
    }

    // Four tile-and-chair stacks on y = 1 with the first leading along dx; the ids decide whether the front or the back runs first.
    private static (Room Room, Item[] Line) StackLine(int frontX, int dx, bool frontFirst)
    {
        var (room, map, items) = World(heightmap: string.Join('\r', Enumerable.Repeat(new string('0', 16), 3)));

        return (room, Enumerable.Range(0, 4).SelectMany(i => TileWithChair(map, items, frontX - dx * i, i % 2 == 0,
            (uint)(frontFirst ? 1 + 2 * i : 7 - 2 * i))).ToArray());
    }

    private static void AssertStackLine(Item[] line, int[] xs)
    {
        Assert.Equal(xs.SelectMany(x => new[] { x, x }), line.Select(item => item.GetX));
        Assert.Equal(xs.SelectMany(_ => new[] { 0.0, 0.5 }), line.Select(item => item.GetZ));
    }

    private static WiredModernAction StackBox(Room room, string name, int direction, Item[] movers, int turn = 0)
    {
        var action = ActionBox(room, name);
        int[] ints = name switch
        {
            "wf_act_rel_mov" => [direction == 2 ? 1 : 0, 1, 1, 0, 100],
            "wf_act_move_furni_as_group" => [direction, 100],
            "wf_act_move_to_dir" => [direction, turn, 100, 0],
            _ => [direction, 0, 100, 0]
        };
        var proposed = SavePacket(ints, movers.Select(item => item.Id).ToArray(), 0);

        if (name == "wf_act_move_furni_as_group") {
            var offset = WiredRoomOperations.Offset(direction);
            proposed = proposed with { IntParams = [0, offset.X, offset.Y, 100, 101, 0], SecondarySelectedItems = [movers[0].Id] };
            LoadStoredDirectionalGroup(action, proposed);
        }
        else if (name is "wf_act_move_rotate" or "wf_act_move_to_dir") {
            LoadStoredRuntime(action, name, proposed);
        }
        else {
            Assert.True(WiredNativeTestSupport.TrySavePrepared(action, proposed, TestWiredConfigurationStore.Instance, out var error), error);
        }

        return action;
    }

    private static bool StackPulse(Room room, WiredModernAction action, Item[] movers, Item? other = null,
        Plus.HabboHotel.Items.Wired.Modern.Addons.WiredPhysicsPolicy? physics = null)
    {
        var context = Context(room, new(WiredEventKind.Use), movers.Append(other).OfType<Item>().OrderBy(item => item.Id).ToArray(), []);
        context.Policy.Addons.DisableAnimation = true;
        context.Policy.Addons.Physics = physics;

        return action.Execute(context);
    }

    // Seven stackable color tiles on y = 1 with line[0] leading along dx; the ids decide whether the front or the back runs first.
    private static (Room Room, Item[] Line) TileLine(int frontX, int dx, int spacing, bool frontFirst)
    {
        var (room, map, items) = World(heightmap: string.Join('\r', Enumerable.Repeat(new string('0', 16), 3)));
        var line = new Item[7];

        for (var i = 0; i < line.Length; i++) {
            var x = frontX - dx * spacing * i;
            var item = MakeItem((uint)(frontFirst ? i + 1 : line.Length - i), "color_tile");
            item.Definition.Stackable = true;
            item.SetState(x, 1, 0, Gamemap.GetAffectedTiles(1, 1, x, 1, 0));
            items[item.Id] = item;
            map.AddToMap(item);
            line[i] = item;
        }

        return (room, line);
    }

    private static WiredModernAction LineBox(Room room, string name, int direction, Item[] line, int turn = 0)
    {
        var action = ActionBox(room, name);
        int[] ints = name == "wf_act_move_to_dir" ? [direction, turn, 100, 0] : [direction, 0, 100, 0];
        LoadStoredRuntime(action, name, SavePacket(ints, line.Select(item => item.Id).ToArray(), 0));

        return action;
    }

    // One repeater pulse; blockedBySelf is the physics add-on's "blocked by furniture" sourcing the movers themselves.
    // Picked furni resolve in ascending id order, as the room's lookup does.
    private static bool Pulse(Room room, WiredModernAction action, Item[] line, bool blockedBySelf)
    {
        var context = Context(room, new(WiredEventKind.Use), line.OrderBy(item => item.Id).ToArray(), []);
        context.Policy.Addons.DisableAnimation = true;

        if (blockedBySelf) {
            context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int>(), line.Select(item => item.Id).ToHashSet());
        }

        return action.Execute(context);
    }

    [Fact]
    public void ChaseQueriesNearestWithinThreeAndOrdersLongAxisFirst()
    {
        var (room, _, _) = World();
        var item = MakeItem(1, "test");
        var near = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 2, Y = 1 };
        var far = new RoomUser(2, 0, 8, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 4, Y = 0 };
        Assert.Same(near, WiredDirectionalActions.Nearest(item, [far, near]));
        Assert.Null(WiredDirectionalActions.Nearest(item, [far]));
        Assert.Equal(new[] { new Point(1, 0), new Point(0, 1) }, WiredDirectionalActions.Steps(item, near, false));
        Assert.Equal(new[] { new Point(-1, 0), new Point(0, -1) }, WiredDirectionalActions.Steps(item, near, true));
    }

    [Fact]
    public void WiredFreezePreservesExistingGameFreezeAndConsumesTeleportCancelFlag()
    {
        var (room, _, _) = World();
        var user = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var state = new WiredAvatarState();
        user.SetStatus("mv", "1,1,0");
        user.IsWalking = true;
        Assert.True(state.FreezeUser(user, 0, false));
        Assert.True(user.Frozen);
        Assert.False(user.CanWalk);
        Assert.False(user.IsWalking);
        Assert.False(user.HasStatus("mv"));
        Assert.False(state.Thaw(user, teleport: true));
        Assert.True(user.Frozen);
        state.FreezeUser(user, 0, true);
        Assert.True(state.Thaw(user, teleport: true));
        Assert.False(user.Frozen);
        Assert.True(user.CanWalk);
        user.Frozen = true;
        user.CanWalk = false;
        state.FreezeUser(user, 0, true);
        state.Thaw(user);
        Assert.True(user.Frozen);
        Assert.False(user.CanWalk); // A game freeze is independently owned.
    }

    [Theory]
    [InlineData(4, 0, -1)]
    [InlineData(6, 0, 1)]
    [InlineData(8, 1, -1)]
    [InlineData(11, -1, -1)]
    public void FurnitureMoveFollowsAirEditorDirectionNumbers(int raw, int dx, int dy)
    {
        var item = MakeItem(1, "test");
        var moves = new List<Point>();
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [raw, 0, 100] }, [item], [], [],
            (_, x, y, _, _) => { moves.Add(new(x, y)); return true; }, (_, _, _, _, _) => false, (_, _) => throw new Exception()));
        Assert.Equal(new Point(dx, dy), Assert.Single(moves));
    }

    [Theory]
    [InlineData(0, 0, -1)]
    [InlineData(1, 1, -1)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 1, 1)]
    [InlineData(4, 0, 1)]
    [InlineData(5, -1, 1)]
    [InlineData(6, -1, 0)]
    [InlineData(7, -1, -1)]
    public void StoredMoveDirectionsUseActualDirectionGrid(int direction, int dx, int dy)
    {
        var item = MakeItem(1, "test");
        var moved = Point.Empty;
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [direction, 0, 100, 0] }, [item], [], [],
            (_, x, y, _, _) => { moved = new(x, y); return true; }, (_, _, _, _, _) => throw new Exception(), (_, _) => throw new Exception()));
        Assert.Equal(new Point(dx, dy), moved);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 2)]
    [InlineData(4, 6)]
    public void StoredMoveTurnsRotateAsTheEditorLabels(int turn, int expected)
    {
        var item = MakeItem(1, "test");
        item.Rotation = 0;
        var rotation = -1;
        Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [-1, turn, 100, 0] }, [item], [], [],
            (_, _, _, value, _) => { rotation = value; return true; }, (_, _, _, _, _) => false, (_, _) => { }));
        Assert.Equal(expected, rotation);
    }

    [Fact]
    public async Task MoveRotateEditorReopensEveryCurrentChoiceAndUnchangedResaveKeepsSettings()
    {
        int[] storedDirection = [-1, 8, 9, 10, 0, 2, 4, 6, 1, 3, 5, 7];
        int[] storedTurn = [0, 2, 4, 6];
        Point[] compass = [new(0, -1), new(1, 0), new(0, 1), new(-1, 0), new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)];

        for (var movement = 0; movement <= 11; movement++) {
            for (var rotation = 0; rotation <= 3; rotation++) {
                var f = new MovementOracleSession("wf_act_move_rotate");
                await f.Save([movement, rotation], [8, 9], 4);
                var saved = f.Action.Configuration;
                Assert.Equal(new[] { storedDirection[movement], storedTurn[rotation], 100, 0 }, saved.IntParams.ToArray());

                if (movement >= 4) {
                    Assert.Equal(compass[movement - 4], WiredRoomOperations.Offset(saved.IntParams[0]));
                }

                var editor = f.Open();
                AssertMovementOracleReply(editor, false, [movement, rotation], [8, 9], 4);
                Assert.Same(saved, f.Action.Configuration);
                var writes = f.Store.Saves.Count;
                var published = f.Published;
                var pending = f.SeedPending();
                f.Client.Packets.Clear();
                await f.Save(editor.Owned, editor.Primary, editor.Delay);
                f.AssertExplicitSave(saved, writes, published, pending);
                Assert.Equal(saved.IntParams.AsEnumerable(), f.Action.Configuration.IntParams);
                Assert.Equal(saved.SelectedItems.AsEnumerable(), f.Action.Configuration.SelectedItems);
                Assert.Equal(saved.FurniSources, f.Action.Configuration.FurniSources);
                Assert.Equal(saved.Delay, f.Action.Configuration.Delay);
            }
        }
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void StoredRandomTurnIsAQuarterTurnEitherWay(int start)
    {
        for (var attempt = 0; attempt < 32; attempt++) {
            var item = MakeItem(1, "test");
            item.Rotation = start;
            var rotation = -1;
            Assert.True(new WiredMovementActions().Execute("wf_act_move_rotate", new() { IntParams = [-1, 6, 100, 0] }, [item], [], [],
                (_, _, _, value, _) => { rotation = value; return true; }, (_, _, _, _, _) => false, (_, _) => { }));
            Assert.Contains(rotation, new[] { (start + 2) % 8, (start + 6) % 8 });
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 6)]
    [InlineData(true, 1)]
    [InlineData(true, 4)]
    public void RandomTurnKeepsAValidRotationAndTheMoveLandsInTheActualRoom(bool extraRot, int start)
    {
        var (room, map, items) = World(new RecordingPlacementStore());
        var mover = MakeItem(1, "test");
        mover.Definition.ExtraRot = extraRot;
        mover.SetState(0, 1, 0, Gamemap.GetAffectedTiles(1, 1, 0, 1, start));
        mover.Rotation = start;
        items[1] = mover;
        map.AddToMap(mover);
        var east = ActionBox(room, "wf_act_move_rotate");
        var west = ActionBox(room, "wf_act_move_rotate");
        LoadStoredRuntime(east, "wf_act_move_rotate", new() { IntParams = [5, 3, 100], SelectedItems = [1] });
        LoadStoredRuntime(west, "wf_act_move_rotate", new() { IntParams = [7, 3, 100], SelectedItems = [1] });

        for (var step = 0; step < 16; step++) {
            var before = mover.Rotation;
            var box = step % 2 == 0 ? east : west;
            Assert.True(box.Execute(Context(room, new(WiredEventKind.Use), [mover], [])));
            Assert.Equal(new Point(step % 2 == 0 ? 1 : 0, 1), new Point(mover.GetX, mover.GetY));
            Assert.Contains(mover.Rotation, new[] { (before + 2) % 8, (before + 6) % 8 });
            Assert.True(WiredRoomOperations.ValidRotation(mover, mover.Rotation));
            Assert.Equal(start % 2, mover.Rotation % 2);
        }
    }




    [Fact]
    public void RuntimeSaveSetupRequiresAllCanonicalCountedTailsAndExactEof()
    {
        var valid = SavePacket([2, 0, 100, 0], [8], 4);
        Assert.Equal(new[] { 2, 0, 100, 0 }, valid.IntParams.ToArray());
        Assert.Equal(new uint[] { 8 }, valid.SelectedItems.ToArray());
        Assert.Equal(4, valid.Delay);
        Assert.Null(valid.Origin);
        object[] complete = [4, 2, 0, 100, 0, "", 1, 8, 4, 0, 0, 0, 0];
        Assert.False(WiredLegacyProtocol.TryRead(Request(complete[..^3]), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Request([.. complete, 1]), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Request(0, "", 1, 0, 0, 0, 0, 0, 0), WiredBoxCategory.Action, out _));
        Assert.False(WiredLegacyProtocol.TryRead(Request(0, "", 1, int.MinValue, 0, 0, 0, 0, 0), WiredBoxCategory.Action, out _));
    }



    [Theory]
    [InlineData("wrong_name", 1, 1)]
    [InlineData("wf_act_move_rotate", 3, 3)]
    [InlineData("wf_act_move_rotate", 1, 2)]
    [InlineData("wf_act_move_rotate", 2, 1)]
    public void StoredRuntimeSetupRefusesWrongNameAndSchemaBeforeInstallation(string name, int rowVersion, int jsonVersion)
    {
        var (room, _, _) = World();
        var box = ActionBox(room, "wf_act_move_rotate");
        var original = box.Configuration;
        var raw = new WiredConfiguration { Version = jsonVersion, IntParams = [5, 3, 100], SelectedItems = [1] };
        var store = new WiredConfigurationStore(new StoredRuntimeRowsDatabase([new(box.Item.Id, name, rowVersion,
            System.Text.Json.JsonSerializer.Serialize(raw))]));
        var failure = Assert.ThrowsAny<Exception>(() => store.Load(box.Item.Id, box.Descriptor));
        Assert.True(failure is InvalidDataException or System.Text.Json.JsonException);
        Assert.Same(original, box.Configuration);
        Assert.Null(original.Origin);
    }


    private sealed record MovementOracleReply(int Limit, int[] Primary, int[] Secondary, int Sprite, uint ItemId,
        string Text, int[] Owned, string[] Variables, int[] Furni, int[] Users, int Code, int Delay,
        bool HasMetadata, int[][] FurniAllowed, int[][] UsersAllowed, int[] FurniDefaults, int[] UserDefaults,
        bool AllowWall, int ContextCount, int[] OwnedDefaults);

    private static MovementOracleReply DecodeMovementOracleReply(byte[] body)
    {
        var packet = new FlashIncomingPacket { Buffer = body };
        var limit = packet.ReadInt();
        var primary = Ints();
        var secondary = Ints();
        var sprite = packet.ReadInt();
        var id = packet.ReadUInt();
        var text = packet.ReadString();
        var owned = Ints();
        var variables = Enumerable.Range(0, Count()).Select(_ => packet.ReadString()).ToArray();
        var furni = Ints();
        var users = Ints();
        var code = packet.ReadInt();
        var delay = packet.ReadInt();
        var metadata = packet.ReadBool();
        Assert.True(metadata);
        var furniAllowed = Groups();
        var usersAllowed = Groups();
        var furniDefaults = Ints();
        var userDefaults = Ints();
        var wall = packet.ReadBool();
        var contexts = packet.ReadInt();
        // These two bounded cards expose no context records; unknown contexts are never invented.
        Assert.Equal(0, contexts);
        var defaults = Ints();
        Assert.False(packet.HasDataRemaining());

        return new(limit, primary, secondary, sprite, id, text, owned, variables, furni, users, code, delay,
            metadata, furniAllowed, usersAllowed, furniDefaults, userDefaults, wall, contexts, defaults);

        int Count()
        {
            var count = packet.ReadInt();
            Assert.InRange(count, 0, 100);

            return count;
        }
        int[] Ints() => Enumerable.Range(0, Count()).Select(_ => packet.ReadInt()).ToArray();
        int[][] Groups() => Enumerable.Range(0, Count()).Select(_ => Ints()).ToArray();
    }

    private static void AssertMovementOracleReply(MovementOracleReply reply, bool direction, int[] owned, int[] picks, int delay)
    {
        Assert.Equal(100, reply.Limit);
        Assert.Equal(picks, reply.Primary);
        Assert.Empty(reply.Secondary);
        Assert.Equal(0, reply.Sprite);
        Assert.Equal(100u, reply.ItemId);
        Assert.Equal("", reply.Text);
        Assert.Equal(owned, reply.Owned);
        Assert.Empty(reply.Variables);
        Assert.Equal(new[] { 100 }, reply.Furni);
        Assert.Empty(reply.Users);
        Assert.Equal(direction ? 13 : 4, reply.Code);
        Assert.Equal(delay, reply.Delay);
        Assert.True(reply.HasMetadata);
        Assert.Equal(new[] { 0, 100, 200, 201 }, Assert.Single(reply.FurniAllowed));
        Assert.Empty(reply.UsersAllowed);
        Assert.Equal(new[] { 100 }, reply.FurniDefaults);
        Assert.Empty(reply.UserDefaults);
        Assert.False(reply.AllowWall);
        Assert.Equal(0, reply.ContextCount);
        // Rotate's two-field/default/wall footer is the explicitly supported LOCAL subset.
        Assert.Equal(direction ? new[] { 0, 0, 1 } : new[] { 0, 0 }, reply.OwnedDefaults);
    }

    private sealed class MovementOracleClient : GameClient
    {
        public List<(uint Header, byte[] Body)> Packets { get; } = [];
        public MovementOracleClient() : base(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
        {
            Revision = new Revision
            {
                InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields()
                    .Where(field => field.IsLiteral && field.FieldType == typeof(uint))
                    .Select(field => (uint)field.GetRawConstantValue()!).Distinct().ToDictionary(id => id)
            };
            SendCallback = _ => false;
        }
        internal override (bool Complete, bool Malformed, uint MessageId, int HeaderLength, int Length) GetMessageIdAndPacketLength(ReadOnlyMemory<byte> buffer) =>
            (true, false, 0, 0, 0);
        public override void CreateHeader(Memory<byte> memory, uint messageId)
        {
            FlashGameClient.EncodeInt32(memory, memory.Length - 4, 0);
            FlashGameClient.EncodeInt16(memory, (short)messageId, 4);
            Packets.Add((messageId, memory[6..].ToArray()));
        }
    }

    private sealed class MovementOracleStore(IWiredConfigurationStore? durable = null) : IWiredConfigurationStore
    {
        public readonly Dictionary<uint, StoredRuntimeRow> Rows = [];
        public readonly List<WiredConfiguration> Saves = [];
        public Action? AfterDurableSave;
        public WiredConfiguration? Load(uint id, WiredBoxDescriptor descriptor) => durable != null
            ? durable.Load(id, descriptor) : new WiredConfigurationStore(new StoredRuntimeRowsDatabase(Rows.Values)).Load(id, descriptor);
        public void Save(uint id, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
        {
            durable?.Save(id, descriptor, configuration);
            AfterDurableSave?.Invoke();
            Saves.Add(configuration);
            var native = configuration.Origin!.Native!;
            Rows[id] = new(id, descriptor.CanonicalName, 2, System.Text.Json.JsonSerializer.Serialize(native));
        }
        public void Reset(IReadOnlyCollection<uint> ids) => throw new InvalidOperationException("Unexpected editor reset.");
    }

    private sealed class MovementOracleLegacyDatabase : IDatabase
    {
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            var connection = DispatchProxy.Create<IDbConnection, RecordingProxy>();
            ((RecordingProxy)(object)connection).InvokeMethod = (method, _) => method.Name switch
            {
                "get_State" => ConnectionState.Open,
                "get_ConnectionString" => "empty-persisted-legacy-editor-fixture",
                "Dispose" or "Close" => null,
                "CreateCommand" => Command(),
                _ => throw new NotSupportedException(method.Name)
            };

            return connection;
        }
        private static IDbCommand Command()
        {
            var parameters = new MySqlCommand().Parameters;
            var command = DispatchProxy.Create<IDbCommand, RecordingProxy>();
            ((RecordingProxy)(object)command).InvokeMethod = (method, args) =>
            {
                switch (method.Name) {
                    case "set_CommandText":
                        Assert.Equal("SELECT items,delay,`string` AS StringData,`bool` AS BoolData FROM wired_items WHERE id=@id LIMIT 1", args![0]);

                        return null;
                    case "get_Parameters":
                        return parameters;
                    case "CreateParameter":
                        return new MySqlParameter();
                    case "Dispose":
                    case "set_CommandTimeout":
                    case "set_CommandType":
                        return null;
                    case "ExecuteReader":
                        var table = new DataTable();
                        table.Columns.Add("Items", typeof(string));
                        table.Columns.Add("Delay", typeof(int));
                        table.Columns.Add("StringData", typeof(string));
                        table.Columns.Add("BoolData", typeof(bool));
                        Assert.Equal(100u, Convert.ToUInt32(parameters["id"].Value));
                        table.Rows.Add("", 0, "", false);

                        return table.CreateDataReader();
                    default:
                        throw new NotSupportedException(method.Name);
                }
            };

            return command;
        }
    }

    private sealed class MovementOracleSession
    {
        public readonly Room Room;
        public readonly Gamemap Map;
        public readonly ConcurrentDictionary<uint, Item> Items;
        public readonly MovementOracleClient Client = new();
        public readonly MovementOracleStore Store;
        public readonly WiredComponent Wired;
        public readonly WiredStackEngine Engine;
        public int Published;
        public IWiredItem Box => Wired.TryGet(100, out var box) ? box : throw new InvalidOperationException("Missing registered box.");
        public WiredModernAction Action => Assert.IsType<WiredModernAction>(Box);
        public MovementOracleSession(string name, IWiredConfigurationStore? durable = null, StoredRuntimeRow? row = null)
        {
            (Room, Map, Items) = World();
            Room.Id = 42;
            Room.OwnerId = 7;
            Room.OwnerName = "owner";
            Room.Type = "private";
            Store = new(durable);

            if (row != null) {
                Store.Rows[row.ItemId] = row;
            }

            Wired = new(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance,
                Store, new MovementOracleLegacyDatabase(), TestWiredRewardService.Instance, TestBotManagementStore.Instance,
                TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Room, Wired);
            Engine = (WiredStackEngine)typeof(WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Wired)!;
            typeof(WiredStackEngine).GetField("_now", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Engine, (Func<long>)(() => 1000));
            Engine.ConfigurationPublished = _ => Published++;
            var item = MakeItem(100, name);
            item.UserId = 7;
            item.RoomId = Room.Id;
            item.Definition.InteractionType = InteractionType.WiredEffect;
            item.Definition.WiredType = name == "wf_act_move_rotate" ? WiredBoxType.EffectMoveAndRotate : WiredBoxType.None;
            item.SetState(2, 2, 0, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
            var picks = new[] { MakeItem(8, "test"), MakeItem(9, "test") };

            for (var i = 0; i < picks.Length; i++) {
                picks[i].UserId = 7;
                picks[i].RoomId = Room.Id;
                picks[i].Definition.Stackable = true;
                picks[i].Definition.Walkable = true;
                picks[i].SetState(1, 1 + i, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1 + i, 0));
            }

            Room.GetRoomItemHandler().LoadFurniture([item, .. picks]);
            Assert.Same(Room, item.GetRoom());
            Assert.Same(item, Box.Item);
            Client.SetHabbo(new Habbo { Id = 7, Username = "owner", CurrentRoom = Room, Access = Plus.HabboHotel.Permissions.UserAccess.Empty });
            Assert.True(Wired.Settings.CanModify(Client));
            Client.Packets.Clear();
        }
        public Task Save(int[] owned, int[]? picks = null, int delay = 0, int source = 100, object[]? suffix = null)
        {
            picks ??= [];
            object[] body = [100, owned.Length, .. owned.Cast<object>(), "", picks.Length, .. picks.Cast<object>(), delay, 1, source, 0, 0, 0, .. suffix ?? []];

            return new SaveWiredEffectConfigEvent(new WiredConfigurationService(Store, null!, TestLogging.For<WiredConfigurationService>())).Parse(Client, Request(body));
        }
        public MovementOracleReply Open()
        {
            Client.Packets.Clear();
            Box.Item.Interactor.OnTrigger(Client, Box.Item, 0, true);
            var packet = Assert.Single(Client.Packets.Where(packet => packet.Header == ServerPacketHeader.WiredEffectConfigComposer));

            return DecodeMovementOracleReply(packet.Body);
        }
        public int SeedPending()
        {
            var item = MakeItem(101, "wf_trg_game_starts");
            item.RoomId = Room.Id;
            item.SetState(2, 2, 1, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
            typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, Room);
            Items[item.Id] = item;
            var trigger = new WiredModernTrigger(Room, item, Descriptor("wf_trg_game_starts"));
            WiredNativeTestSupport.InstallRuntime(trigger, WiredTriggerConfiguration.Defaults("wf_trg_game_starts"));
            Assert.True(Wired.AddBox(trigger));
            Assert.True(Engine.Enqueue(new(WiredEventKind.GameStart)));
            var pending = Engine.ReadStats().Pending;
            Assert.True(pending > 0);

            return pending;
        }
        public void AssertExplicitSave(WiredConfiguration before, int writes, int published, int pending)
        {
            Assert.NotSame(before, Action.Configuration);
            Assert.Equal(before.IntParams.ToArray(), Action.Configuration.IntParams.ToArray());
            Assert.Equal(writes + 1, Store.Saves.Count);
            Assert.Equal(published + 1, Published);
            Assert.Contains(Client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        }
    }

    [Fact]
    public async Task MovementOracleActualHandlerRequiresTwoOwnedRotateFieldsRatherThanInlineSource()
    {
        var f = new MovementOracleSession("wf_act_move_rotate");
        var original = f.Box;
        await f.Save([5, 1, 100], [8]);
        Assert.Same(original, f.Box);
        Assert.Empty(f.Store.Saves);
        Assert.DoesNotContain(f.Client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
        f.Client.Packets.Clear();
        await f.Save([5, 1], [8]);
        Assert.Single(f.Store.Saves);
        AssertMovementOracleReply(f.Open(), false, [5, 1], [8], 0);
    }

    [Fact]
    public void MovementOracleIndependentFooterSeparatesLocalDefaultsFromSourceDefaults()
    {
        var f = new MovementOracleSession("wf_act_move_rotate");
        var reply = f.Open();
        AssertMovementOracleReply(reply, false, [0, 0], [], 0);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("movement")]
    [InlineData("negative-movement")]
    [InlineData("rotation")]
    [InlineData("source")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    public async Task MovementOracleMalformedNativeRequestsPreserveInstalledConfigurationAndPending(string change)
    {
        var f = new MovementOracleSession("wf_act_move_rotate");
        await f.Save([5, 1], [8]);
        var saved = f.Action.Configuration;
        var row = f.Store.Rows[100];
        var pending = f.SeedPending();
        var published = f.Published;
        int[] owned = change switch { "count" => [5], "movement" => [12, 1], "negative-movement" => [-1, 1], "rotation" => [5, 4], _ => [5, 1] };
        object[] values = [100, owned.Length, .. owned.Cast<object>(), "", 1, 8, 0, 1, change == "source" ? 900 : 100, 0, 0, 0];

        if (change == "truncated") {
            values = values[..^1];
        }
        else if (change == "trailing") {
            values = [.. values, 999];
        }

        f.Client.Packets.Clear();
        await new SaveWiredEffectConfigEvent(new WiredConfigurationService(f.Store, null!, TestLogging.For<WiredConfigurationService>()))
            .Parse(f.Client, Request(values));
        Assert.Same(saved, f.Action.Configuration);
        Assert.Equal(row, f.Store.Rows[100]);
        Assert.Single(f.Store.Saves);
        Assert.Equal(published, f.Published);
        Assert.Equal(pending, f.Engine.ReadStats().Pending);
        Assert.DoesNotContain(f.Client.Packets, packet => packet.Header == ServerPacketHeader.HideWiredConfigComposer);
    }

    [Fact]
    public async Task MovementOracleNativeZeroZeroIsValidAndKeepsNoMotionSemantics()
    {
        var f = new MovementOracleSession("wf_act_move_rotate");
        await f.Save([0, 0], [8]);
        Assert.Single(f.Store.Saves);
        AssertMovementOracleReply(f.Open(), false, [0, 0], [8], 0);
        Assert.Equal(new[] { -1, 0, 100, 0 }, f.Action.Configuration.IntParams.ToArray());
        var before = f.Items[8].Coordinate;
        Assert.False(f.Action.Execute(Context(f.Room, new(WiredEventKind.Use), [f.Items[8]], [])));
        Assert.Equal(before, f.Items[8].Coordinate);
        Assert.Equal(0, f.Items[8].Rotation);
    }


    private static WiredConfiguration SavePacket(int[] ints, uint[] selected, int delay)
    {
        object[] values = [ints.Length, .. ints.Cast<object>(), "", selected.Length, .. selected.Select(id => (object)(int)id), delay, 0, 0, 0, 0];
        Assert.True(WiredLegacyProtocol.TryRead(Request(values), WiredBoxCategory.Action, out var configuration));

        return configuration;
    }

    private static void LoadStoredDirectionalGroup(WiredModernAction box, WiredConfiguration configuration)
    {
        Assert.Equal("wf_act_move_furni_as_group", box.Descriptor.CanonicalName);
        LoadStoredRuntime(box, "wf_act_move_furni_as_group", configuration);
        Assert.Equal(configuration.IntParams.ToArray(), box.Configuration.IntParams.ToArray());
        Assert.Equal(configuration.SelectedItems.ToArray(), box.Configuration.SelectedItems.ToArray());
    }


    /// <summary>Test support: installs a runtime-shaped draft for a mapped action by deriving its native record.</summary>
    internal static void LoadStoredRuntime(WiredModernAction box, string storedName, WiredConfiguration configuration)
    {
        Assert.Equal(storedName, box.Descriptor.CanonicalName);
        var handler = box.Instance.GetRoomItemHandler();
        var native = WiredNativeTestSupport.FromRuntime(box.Descriptor, configuration, id => handler?.GetItem(id)?.IsWallItem ?? false);
        Assert.True(WiredNativeEditorProjection.TryCompile(box.Item.Id, box.Descriptor, native, out var runtime));
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, runtime, out var validated, out var error), error);
        box.ApplyConfiguration(validated);
    }

    internal sealed record StoredRuntimeRow(uint ItemId, string Name, int Version, string Json);

    internal sealed class StoredRuntimeRowsDatabase(IEnumerable<StoredRuntimeRow> rows) : IDatabase
    {
        private readonly ImmutableDictionary<uint, StoredRuntimeRow> _rows = rows.ToImmutableDictionary(row => row.ItemId);
        public bool IsConnected() => true;
        public IDbConnection Connection()
        {
            var state = ConnectionState.Closed;
            var connection = DispatchProxy.Create<IDbConnection, RecordingProxy>();
            ((RecordingProxy)(object)connection).InvokeMethod = (method, _) => method.Name switch
            {
                "get_State" => state,
                "get_ConnectionString" => "stored-v1-runtime-fixture",
                "Open" => Change(ConnectionState.Open),
                "Close" or "Dispose" => Change(ConnectionState.Closed),
                "CreateCommand" => Command(),
                _ => throw new NotSupportedException(method.Name)
            };

            return connection;

            object? Change(ConnectionState next)
            {
                state = next;

                return null;
            }
        }

        private IDbCommand Command()
        {
            var parameters = new MySqlCommand().Parameters;
            var command = DispatchProxy.Create<IDbCommand, RecordingProxy>();
            ((RecordingProxy)(object)command).InvokeMethod = (method, args) =>
            {
                switch (method.Name) {
                    case "set_CommandText":
                        Assert.Contains("FROM wired_item_configurations WHERE item_id=@Id", (string)args![0]!);

                        return null;
                    case "get_Parameters":
                        return parameters;
                    case "CreateParameter":
                        return new MySqlParameter();
                    case "Dispose":
                    case "set_CommandTimeout":
                    case "set_CommandType":
                        return null;
                    case "ExecuteReader":
                        var itemId = Convert.ToUInt32(parameters["Id"].Value);
                        var table = new DataTable();
                        table.Columns.Add("BoxName", typeof(string));
                        table.Columns.Add("Version", typeof(int));
                        table.Columns.Add("Json", typeof(string));

                        if (_rows.TryGetValue(itemId, out var row)) {
                            table.Rows.Add(row.Name, row.Version, row.Json);
                        }

                        return table.CreateDataReader();
                    default:
                        throw new NotSupportedException(method.Name);
                }
            };

            return command;
        }
    }

    private static (int[] Ints, uint[] Selected, int Delay) EditorFields(WiredEditorSnapshot snapshot, int editorCode = 4)
    {
        var fields = new List<object>();
        var packet = DispatchProxy.Create<IOutgoingPacket, RecordingProxy>();
        ((RecordingProxy)(object)packet).InvokeMethod = (_, args) => { fields.Add(args![0]!); return null; };
        new WiredConfiguredConfigComposer(snapshot).Compose(packet);
        // false, furni limit, picks, sprite, item id, text, ints, selection code, editor code, delay, blocked sprites.
        var selected = fields.Skip(3).Take((int)fields[2]).Cast<uint>().ToArray();
        var at = 3 + selected.Length + 3;
        var ints = fields.Skip(at + 1).Take((int)fields[at]).Cast<int>().ToArray();
        at += 1 + ints.Length;
        Assert.Equal(editorCode, (int)fields[at + 1]);
        Assert.Equal(at + 4, fields.Count);

        return (ints, selected, (int)fields[at + 2]);
    }

    [Fact]
    public void RoomForwardingResolvesActualLinkSectionsPairsAndFallbackInOrder()
    {
        var link = MakeItem(1, "link");
        link.ExtraData = new MapDataFormat(new() { ["internalLink"] = "23" });
        Assert.Equal(new WiredRoomForwarding.Destination(23), WiredRoomForwarding.Resolve([link], "99", _ => throw new Exception(), _ => throw new Exception(), TestLogging.Logger));
        link.ExtraData = new LegacyDataFormat { Data = "{\"room_linker\":{\"ItemId\":17}}" };
        Assert.Equal(new WiredRoomForwarding.Destination(42, 17), WiredRoomForwarding.Resolve([link], "99", id => id == 17 ? 42u : 0, _ => throw new Exception(), TestLogging.Logger));
        var tele = MakeItem(2, "tele");
        tele.Definition.InteractionType = InteractionType.Teleport;
        Assert.Equal(new WiredRoomForwarding.Destination(42, 17), WiredRoomForwarding.Resolve([tele], "99", id => id == 17 ? 42u : 0, id => id == 2 ? 17u : 0, TestLogging.Logger));
        Assert.Equal(new WiredRoomForwarding.Destination(99), WiredRoomForwarding.Resolve([], "99", _ => 0, _ => 0, TestLogging.Logger));
        Assert.Null(WiredRoomForwarding.Resolve([], "2147483648", _ => 0, _ => 0, TestLogging.Logger));
        link.ExtraData = new LegacyDataFormat { Data = "{\"room_linker\":{\"RoomId\":\"bad\",\"ItemId\":[]}}" };
        Assert.Equal(new WiredRoomForwarding.Destination(99), WiredRoomForwarding.Resolve([link], "99", _ => throw new Exception(), _ => throw new Exception(), TestLogging.Logger));
    }

    [Fact]
    public void ConfiguredRoomForwardingSendsActualClientPacketAndLeavesRoomChecksToEntry()
    {
        using var fixture = new TeleportFixture();
        var sent = 0;
        fixture.Client.SendCallback = _ => { sent++; return true; };
        var action = ActionBox(fixture.Room, "wf_act_teleport_to_room");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new() { IntParams = [0, 100], Text = "42" }, out var config, out _));
        action.ApplyConfiguration(config);
        var context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = fixture.User }, [], [fixture.User]);
        context.Triggering.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context));
        Assert.Equal(1, sent);
        Assert.Same(fixture.Room, fixture.Habbo.CurrentRoom);
        Assert.False(fixture.Habbo.IsTeleporting);
        Assert.Equal(42u, fixture.Habbo.WiredRoomNetworkDestination);
        fixture.Habbo.CurrentRoom = null;
        Assert.False(action.Execute(context));
        Assert.Equal(1, sent);
    }

    [Fact]
    public void ActualForwardingRecordsNetworkEntryOnlyAfterPacketSendAndDestinationConsumesIt()
    {
        using var f = new TeleportFixture();
        var action = ActionBox(f.Room, "wf_act_teleport_to_room");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new() { IntParams = [0, 100], Text = "42" }, out var config, out _));
        action.ApplyConfiguration(config);
        var context = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, [], [f.User]);
        context.Triggering.UserIds.Add(f.User.VirtualId);
        f.Habbo.CurrentRoom = null;
        Assert.False(action.Execute(context));
        Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        f.Habbo.CurrentRoom = f.Room;
        f.Client.SendCallback = _ => throw new IOException("forward enqueue failed");
        Assert.Throws<IOException>(() => action.Execute(context));
        Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        f.Client.SendCallback = _ => true;
        Assert.True(action.Execute(context));
        Assert.Equal(42u, f.Habbo.WiredRoomNetworkDestination);
        var (destination, _, _) = World();
        destination.Id = 42;
        f.Habbo.Gender = "M";
        f.Habbo.Look = "test";
        f.Habbo.Motto = "";
        f.Habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        f.Habbo.Access = EditorTestSupport.Access([]);
        Assert.True(destination.GetRoomUserManager().AddAvatarToRoom(f.Client));
        var joined = Assert.IsType<RoomUser>(destination.GetRoomUserManager().GetRoomUserByHabbo(f.Habbo.Id));
        Assert.NotSame(f.User, joined);
        Assert.Equal(new WiredRoomEntrySnapshot(WiredRoomEntryMethod.RoomNetwork, 0), joined.WiredRoomEntry);
        Assert.Equal(0u, f.Habbo.WiredRoomNetworkDestination);
        Assert.Equal(default, WiredRoomEntrySnapshot.Capture(destination, f.Habbo));
    }

    [Fact]
    public void RealScoreControllerPublishesPreviousValuesAndDistinctGameQuotas()
    {
        var (room, _, _) = World();
        var state = new WiredGameState();
        var scores = new List<WiredRuntimeEvent>();
        Assert.True(state.GiveScore(room, 1, 7, Team.Red, 10, 1, scores.Add));
        Assert.False(state.GiveScore(room, 1, 7, Team.Red, 10, 1, scores.Add));
        Assert.True(state.GiveScore(room, 1, 8, Team.Red, -20, 1, scores.Add));
        Assert.Equal(10, scores[1].PreviousValue);
        Assert.Equal(0, scores[1].Value);
        Assert.Equal(0, room.GetGameManager().Points[1]);
        Assert.True(state.GiveScore(room, 2, 7, Team.Red, 2, 1, scores.Add));
        state.ResetQuotas();
        Assert.True(state.GiveScore(room, 1, 7, Team.Red, 3, 1, scores.Add));

        for (var index = 0; index < 12; index++) {
            Assert.True(state.GiveScore(room, 3, 7, Team.Red, 1, null, scores.Add));
        }

        Assert.Equal(17, room.GetGameManager().Points[1]);
    }

    [Fact]
    public void BotArrivalUsesActualRoomIdentityAndOnlyFiresOnce()
    {
        var (room, _, items) = World();
        var bot = Bot(room, 7);
        var target = MakeItem(1, "test");
        items[1] = target;
        RoomUsers(room)[7] = bot;
        var targets = new WiredBotTargets();
        targets.Walk(bot, target);
        bot.SetPos(2, 2, 0);
        Assert.Empty(targets.Poll(room));
        bot.SetPos(0, 0, 0);
        Assert.Equal(WiredEventKind.BotReachedFurni, Assert.Single(targets.Poll(room)).Kind);
        Assert.Empty(targets.Poll(room));
        Assert.False(targets.HasTargets);
        targets.Walk(bot, target);
        items[1] = MakeItem(1, "replacement");
        Assert.Empty(targets.Poll(room));
        Assert.False(targets.HasTargets);
        var user = new RoomUser(1, 0, 8, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 0 };
        RoomUsers(room)[8] = user;
        targets.Follow(bot, user);
        Assert.Same(user, Assert.Single(targets.Poll(room)).TargetUser);
        Assert.Empty(targets.Poll(room));
        RoomUsers(room)[8] = new RoomUser(1, 0, 8, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        Assert.Empty(targets.Poll(room));
        Assert.False(targets.HasTargets);
    }

    [Fact]
    public void BotValidationPreservesWidthAndFigureChecksActualTurboShape()
    {
        Assert.True(WiredBotActions.TryValidate("wf_act_bot_talk_to_avatar", new() { IntParams = [1, 11, 100, 2], Text = "Alice\tHello" }, out var config, out _));
        Assert.Equal(11, config.UserSources["users"]);
        Assert.Equal(100, config.UserSources["bots"]);
        Assert.Equal(2, config.IntParams[3]);
        Assert.True(WiredBotActions.FigureWellFormed("hd-180-1.ch-210-66"));
        Assert.False(WiredBotActions.FigureWellFormed("hd-180-script"));
    }

    [Fact]
    public void SelectedUsersArriveTogetherOnOneTeleportDestination()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        var second = f.AddPlayer(8, 2, 0);
        f.SelectPlayers();
        f.Use(0, 200);
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), second.Coordinate);
        var durations = MovementDurations(replies);
        Assert.Equal(2, durations.Length);
        Assert.All(durations, duration => Assert.Equal(500, duration));
        f.Advance(500);
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), second.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void FastTeleportKeepsWhoeverIsAlreadyThereAndStillBringsTheOthers()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        var resident = f.AddPlayer(8, 1, 1);
        f.SelectPlayers();
        f.Use(1, 200);
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), resident.Coordinate);
        Assert.Equal(new[] { 0 }, MovementDurations(replies));
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void FastTeleportMovesAtOnceAndSendsNoAnimationTime()
    {
        using var f = new TeleportFixture();
        f.Use(1, 0);
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Equal(new[] { 0 }, MovementDurations(replies));
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        f.Advance(500);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void TeleportMovesAtOnceAndGlidesForTheStackAnimationTime()
    {
        using var f = new TeleportFixture();
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Equal(new[] { 500 }, MovementDurations(replies));
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        f.Advance(1500);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void TeleportStillRefusesAClosedTile()
    {
        using var f = new TeleportFixture();
        f.Room.GetGameMap().Model.SqState[1, 1] = SquareState.Blocked;
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        f.Advance(500);
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Empty(MovementDurations(replies));
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void TeleportStillRefusesFurniNobodyCanStandOn()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = false;
        f.Room.GetGameMap().AddToMap(f.Target);
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        f.Advance(500);
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Empty(MovementDurations(replies));
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void TeleportCanShareASeatWithTheUserAlreadyOnIt()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.IsSeat = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        var resident = f.AddPlayer(8, 1, 1);
        f.SelectPlayers();
        f.Use(0, 200);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), resident.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TeleportThawsOnlyAFreezeThatEndsOnTeleport(bool cancelOnTeleport)
    {
        using var f = new TeleportFixture();
        Assert.True(WiredAvatarState.For(f.Room).FreezeUser(f.User, 0, cancelOnTeleport));
        Assert.True(f.User.Frozen);
        f.Fire();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(!cancelOnTeleport, f.User.Frozen);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void UserToFurniSlideStillStopsWhenTheDestinationIsOccupied()
    {
        using var f = new TeleportFixture(actionName: "wf_act_user_to_furni", intParams: [100, 0, 1]);
        var resident = f.AddPlayer(8, 1, 1);
        f.Fire();
        f.Advance(500);
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), resident.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2TeleportSharesAnOccupiedDestinationWhenTheFlagAllowsIt()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        var resident = f.AddPlayer(8, 1, 1);
        f.SelectPlayers();
        f.Use(0, 200);
        f.UseExecutor();
        Assert.True(f.Room.UsesV2Movement);
        Assert.False(WiredRoomOperations.RelocateAvatar(f.Room, f.User, 1, 1, false));
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), resident.Coordinate);
        f.FireOwned();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(new Point(1, 1), resident.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2TeleportStillRefusesAClosedTileWithTheFlag()
    {
        using var f = new TeleportFixture();
        f.Room.GetGameMap().Model.SqState[1, 1] = SquareState.Blocked;
        f.UseExecutor();
        Assert.False(WiredRoomOperations.RelocateAvatar(f.Room, f.User, 1, 1, false, ignoreOccupants: true));
        f.FireOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2TeleportStillRefusesFurniNobodyCanStandOnWithTheFlag()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = false;
        f.Room.GetGameMap().AddToMap(f.Target);
        f.UseExecutor();
        Assert.False(WiredRoomOperations.RelocateAvatar(f.Room, f.User, 1, 1, false, ignoreOccupants: true));
        f.FireOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2TeleportCanStandOnAWalkMagicTile()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = false;
        f.Target.Definition.InteractionType = InteractionType.WalkMagicTile;
        f.Room.GetGameMap().AddToMap(f.Target);
        f.UseExecutor();
        f.FireOwned();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2TeleportStillRefusesAPhysicsBlockingFurni()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        f.BlockDestination();
        f.UseExecutor();
        f.FireOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);

        using (RoomOwnerScope.Enter(f.Room)) {
            Assert.True(WiredRoomOperations.RelocateAvatar(f.Room, f.User, 1, 1, false, ignoreOccupants: true));
        }

        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2IncomingTeleportPublishesDestinationAndWalkOnlyAfterTheOwnerMoves()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.Room.GetGameMap().AddToMap(f.Target);
        f.Target.Attach(f.Room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        f.UseExecutor();
        var replies = Capture(f.Habbo.Client);
        Assert.False(RoomOwnerScope.IsOwner(f.Room));
        f.Fire();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Null(f.User.LastItem);
        Assert.Empty(MovementEndpoints(replies));
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.WiredFurniMoveStyleComposer);
        f.DrainOwned();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Same(f.Target, f.User.LastItem);
        Assert.Equal((0, 0, 1, 1, 500), Assert.Single(MovementEndpoints(replies)));
        Assert.Contains(replies, reply => reply.Header == ServerPacketHeader.WiredFurniMoveStyleComposer);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2IncomingTeleportRevalidatesAClosedTileBeforeThePacket()
    {
        using var f = new TeleportFixture();
        f.UseExecutor();
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        f.Room.GetGameMap().Model.SqState[1, 1] = SquareState.Blocked;
        f.DrainOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Empty(MovementEndpoints(replies));
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2IncomingTeleportRevalidatesPhysicsBlockingFurniBeforeThePacket()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Walkable = true;
        f.BlockDestination();
        f.UseExecutor();
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        f.Room.GetGameMap().AddToMap(f.Target);
        f.DrainOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Empty(MovementEndpoints(replies));
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V2IncomingMovementKeepsAWalkingRequestPublishedAfterTheMove(bool slide)
    {
        using var f = slide
            ? new TeleportFixture(actionName: "wf_act_user_to_furni", intParams: [100, 0, 1])
            : new TeleportFixture();
        f.UseExecutor();
        f.User.IsWalking = true;
        f.User.GoalX = 1;
        f.User.GoalY = 0;
        f.User.MoveTo(1, 0);
        var before = f.User.Movement.Commands.Read()!.Sequence;
        f.Fire();
        f.User.MoveTo(0, 1);
        var after = f.User.Movement.Commands.Read()!.Sequence;
        f.DrainOwned();
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(before, f.User.Movement.ConsumedSequence);
        Assert.True(after > f.User.Movement.ConsumedSequence);
        Assert.Equal(after, f.User.Movement.Commands.Read()!.Sequence);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2IncomingTeleportDropsTheMoveWhenTheActorIsRemoved()
    {
        using var f = new TeleportFixture();
        f.UseExecutor();
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        f.Room.GetGameMap().Navigation!.Remove(f.User);
        f.DrainOwned();
        Assert.NotEqual(new Point(1, 1), f.User.Coordinate);
        Assert.Empty(MovementEndpoints(replies));
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void V2IncomingTeleportDropsTheMoveWhenTheActorLifetimeChanges()
    {
        using var f = new TeleportFixture();
        f.UseExecutor();
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        typeof(RoomUser).GetField("_movement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(f.User, new ActorMovementState());
        f.DrainOwned();
        Assert.Equal(new Point(0, 0), f.User.Coordinate);
        Assert.Empty(MovementEndpoints(replies));
        Assert.Empty(f.Errors);
    }

    private static int[] MovementDurations(List<(uint Header, byte[] Body)> replies)
    {
        var durations = new List<int>();

        foreach (var reply in replies) {
            if (reply.Header != ServerPacketHeader.WiredMovementsComposer) {
                continue;
            }

            var packet = new WireReader(reply.Body);
            Assert.Equal(1, packet.Int());
            Assert.Equal(0, packet.Int());
            packet.Skip(4);
            packet.String();
            packet.String();
            packet.Int();
            Assert.Equal(1, packet.Int());
            durations.Add(packet.Int());
            packet.Skip(2);
            Assert.False(packet.Bool());
            packet.End();
        }

        return durations.ToArray();
    }

    private static (int FromX, int FromY, int ToX, int ToY, int Duration)[] MovementEndpoints(List<(uint Header, byte[] Body)> replies)
    {
        var endpoints = new List<(int, int, int, int, int)>();

        foreach (var reply in replies) {
            if (reply.Header != ServerPacketHeader.WiredMovementsComposer) {
                continue;
            }

            var packet = new WireReader(reply.Body);
            Assert.Equal(1, packet.Int());
            Assert.Equal(0, packet.Int());
            var fromX = packet.Int();
            var fromY = packet.Int();
            var toX = packet.Int();
            var toY = packet.Int();
            packet.String();
            packet.String();
            packet.Int();
            Assert.Equal(1, packet.Int());
            var duration = packet.Int();
            packet.Skip(2);
            Assert.False(packet.Bool());
            endpoints.Add((fromX, fromY, toX, toY, duration));
            packet.End();
        }

        return endpoints.ToArray();
    }

    [Fact]
    public void TeleportMovesImmediatelyWhenThePendingQueueHasRoomForOnlyTheFiring()
    {
        using var f = new TeleportFixture(1);
        var replies = Capture(f.Habbo.Client);
        f.Fire();
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.DoesNotContain(replies, reply => reply.Header == ServerPacketHeader.AvatarEffectComposer);
        f.Advance(1500);
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("source")]
    [InlineData("save")]
    [InlineData("visit")]
    public void TeleportAlreadyHappenedSoRemovingTargetSourceOrVisitDoesNotPullTheUserBack(string change)
    {
        using var f = new TeleportFixture();
        f.Fire();
        Assert.Equal(8, f.User.CurrentEffect);
        Assert.Equal(new Point(1, 1), f.User.Coordinate);

        if (change == "target") {
            f.Items.TryRemove(f.Target.Id, out _);
        }

        if (change == "source") {
            f.Engine.Remove(f.Trigger.Item.Id);
        }

        if (change == "save") {
            Assert.True(WiredNativeTestSupport.TryCompileRuntime(f.Action, f.Action.Configuration with { IntParams = [1, 100, 0] }, out var updated));
            Assert.True(f.Engine.PublishConfigured(f.Action, updated, () => { }));
        }

        if (change == "visit") {
            RoomUsers(f.Room)[7] = new RoomUser(1, 0, 7, f.Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
            Assert.IsType<EffectsComponent>(f.Habbo.Effects).CurrentEffect = -1;
        }

        f.Advance(1500);
        Assert.Equal(new Point(1, 1), f.User.Coordinate);
        Assert.Equal(change == "visit" ? -1 : 8, Assert.IsType<EffectsComponent>(f.Habbo.Effects).CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void ActualOverlappingTeleportsAndLaterEffectChangesPreserveTheRightEffect()
    {
        using var f = new TeleportFixture();
        f.Fire();
        f.Fire();
        Assert.Equal(8, f.User.CurrentEffect);
        f.Advance(1500);
        Assert.Equal(8, f.User.CurrentEffect);
        f.User.SetPos(0, 0, 0);
        f.Fire();
        Assert.IsType<EffectsComponent>(f.Habbo.Effects).ApplyEffect(12);
        f.Advance(1500);
        Assert.Equal(12, f.User.CurrentEffect);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void FloorPlacementAndRemovalAttachAndDetachActualCounterController()
    {
        using var fixture = new TeleportFixture();
        var counter = MakeItem(500, "wf_upcounter1");
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(counter, fixture.Room);
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => throw new NotSupportedException(method.Name);

        try {
            databaseField.SetValue(null, database);
            Assert.True(fixture.Room.GetRoomItemHandler().SetFloorItem(null!, counter, 2, 2, 0, true, false, false));
            Assert.True(fixture.Room.GetWired().TryUseCounter(counter, 0));
            fixture.Room.GetRoomItemHandler().RemoveFurniture(null!, counter.Id);
            Assert.Null(fixture.Room.GetRoomItemHandler().GetItem(counter.Id));
            Assert.False(fixture.Room.GetWired().TryUseCounter(counter, 0));
        }
        finally {
            databaseField.SetValue(null, original);
        }
    }

    private static ConcurrentDictionary<int, RoomUser> RoomUsers(Room room) =>
        (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
    private static RoomUser Bot(Room room, int virtualId)
    {
        var bot = new RoomUser(0, 0, virtualId, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot)) };
        bot.BotData.Name = "Alice";
        bot.BotData.Look = "hd-180-1";
        bot.BotData.Gender = "M";

        return bot;
    }
    private sealed class TeleportFixture : IDisposable
    {
        public readonly Room Room; public readonly ConcurrentDictionary<uint, Item> Items;
        public readonly RoomUser User; public readonly Habbo Habbo; public readonly FlashGameClient Client; public readonly Item Target;
        public readonly WiredModernAction Action; public readonly WiredModernTrigger Trigger;
        public readonly WiredStackEngine Engine; public readonly List<Exception> Errors = [];
        public IItemDataManager? DefinitionManager;
        public readonly TestRewardProgress HandRewards = new();
        private readonly object? _originalGame; private long _now;
        public TeleportFixture(int cap = 100, IDatabase? database = null, IGameClientManager? clientsForText = null,
            string actionName = "wf_act_teleport_to", int[]? intParams = null)
        {
            (Room, _, Items) = World();
            var gameField = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
            _originalGame = gameField.GetValue(null);
            var clients = new GameClientManager(null!, null!);
            var game = DispatchProxy.Create<IGame, RecordingProxy>();
            ((RecordingProxy)(object)game).InvokeMethod = (method, _) => method.Name == "get_ClientManager" ? clients : method.Name == "get_ItemManager" ? throw new InvalidOperationException("Global item manager unavailable.") : null;
            gameField.SetValue(null, game);
            var client = new FlashGameClient(TestGameServer.Instance, new FlashPacketFactory(), TestLogging.GameClient)
            {
                Revision = new Revision
                {
                    InternalIdToOutgoingIdMapping = typeof(ServerPacketHeader).GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Where(field => field.FieldType == typeof(uint)).Select(field => (uint)field.GetValue(null)!).Distinct().ToDictionary(id => id, id => id)
                },
                SendCallback = _ => true
            };
            Habbo = (Habbo)RuntimeHelpers.GetUninitializedObject(typeof(Habbo));
            Habbo.Id = 1;
            Habbo.Username = "Alice";
            Habbo.CurrentRoom = Room;
            Habbo.Client = client;
            Client = client;
            Habbo.Effects = new(new FixedTimeProvider(FixedTimeProvider.Epoch));
            typeof(EffectsComponent).GetField("_habbo", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Habbo.Effects, Habbo);
            Habbo.Effects.CurrentEffect = 8;
            client.SetHabbo(Habbo);
            clients.RegisterClient(client, 1, "Alice");
            User = new(1, 0, 7, Room, client, TestChatEmotions.Unused, HandRewards);
            RoomUsers(Room)[7] = User;
            Room.GetGameMap().AddUserToMap(User, new(0, 0));
            var wired = new WiredComponent(Room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance,
                database == null ? TestWiredConfigurationStore.Instance : new WiredConfigurationStore(database),
                database ?? TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance,
                clientsForText ?? TestWiredClients.Empty, TestGroupManager.Empty,
                new TestWiredDefinitions(() => DefinitionManager?.Items ?? throw new InvalidOperationException("No test definitions installed.")),
                TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
            Engine = new(() => _now, box => Items.TryGetValue(box.Item.Id, out var item) && ReferenceEquals(item, box.Item), _ => true, _ => { }, Errors.Add, new() { MaxPendingStacks = cap });
            Engine.BindRuntime(Room, new(() => Items.Values, () => RoomUsers(Room).Values), wired);
            typeof(WiredComponent).GetField("_engine", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(wired, Engine);
            typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Room, wired);
            Target = MakeItem(1, "test");
            Target.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
            Items[1] = Target;
            Trigger = new(Room, MakeItem(101, "wf_trg_enter_room"), Descriptor("wf_trg_enter_room"));
            WiredNativeTestSupport.InstallRuntime(Trigger, WiredTriggerConfiguration.Defaults("wf_trg_enter_room"));
            var actionItem = MakeItem(100, actionName);
            typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(actionItem, Room);
            actionItem.RoomId = Room.Id;
            Action = new(Room, actionItem, Descriptor(actionName), new(),
                evt => wired.Dispatch(evt), wired.DispatchWalkTransition, new(), TestLogging.Logger, TimeProvider.System, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestWiredDefinitions.Unused, TestItemRuntime.Travel);
            var parameters = intParams == null
                ? new WiredConfiguration { IntParams = [0, 100, 0], SelectedItems = [1] }
                : new WiredConfiguration { IntParams = System.Collections.Immutable.ImmutableArray.Create(intParams), SelectedItems = [1] };
            WiredNativeTestSupport.TryValidateRuntime(Action, parameters, out var config, out _);
            Action.ApplyConfiguration(config);
            Items[101] = Trigger.Item;
            Items[100] = Action.Item;
            Engine.Add(Trigger);
            Engine.Add(Action);
        }
        public void Fire()
        {
            Assert.True(Engine.DispatchSynchronously(new WiredRuntimeEvent(WiredEventKind.Enter) { Actor = User }));
            Engine.OnFastCycle();
        }
        public void Advance(int milliseconds)
        {
            _now += milliseconds;
            Engine.OnFastCycle();
        }
        public RoomUser AddPlayer(int virtualId, int x, int y)
        {
            var player = new RoomUser(virtualId, 0, virtualId, Room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = x, Y = y };
            RoomUsers(Room)[virtualId] = player;
            Room.GetGameMap().AddUserToMap(player, new(x, y));

            return player;
        }
        public void SelectPlayers()
        {
            var item = MakeItem(102, "wf_slc_users_bytype");
            var selector = Plus.HabboHotel.Items.Wired.Modern.Selectors.WiredSelectorFactory.Create(Room, item, new(), TestGroupManager.Empty);
            Assert.NotNull(selector);
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(selector, new() { IntParams = [1, 0, 0] }, out var config, out var error), error);
            selector.ApplyConfiguration(config);
            Items[102] = item;
            Assert.True(Engine.Add(selector));
        }
        public void Use(int fast, int userSource)
        {
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(Action, new() { IntParams = [fast, 100, userSource], SelectedItems = [1] }, out var config, out var error), error);
            Action.ApplyConfiguration(config);
        }
        public void UseExecutor()
        {
            var map = Room.GetGameMap();
            var navigation = new RoomNavigation(Room, map.StaticModel, new() { Engine = PathfindingEngine.V2 },
                TestLogging.Navigation, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
            typeof(Gamemap).GetField("<Navigation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(map, navigation);

            using (RoomOwnerScope.Enter(Room)) {
                foreach (var user in RoomUsers(Room).Values.ToArray()) {
                    map.RemoveUserFromMap(user, new(user.X, user.Y));
                    navigation.Admit(user);
                }
            }
        }
        public void FireOwned()
        {
            using (RoomOwnerScope.Enter(Room)) {
                Fire();
            }
        }
        public void DrainOwned()
        {
            using (RoomOwnerScope.Enter(Room)) {
                Room.GetGameMap().Navigation!.DrainCommands();
            }
        }
        public void BlockDestination()
        {
            var item = MakeItem(103, "wf_xtra_mov_physics");
            typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, Room);
            item.RoomId = Room.Id;
            var addon = WiredAddonFactory.Create(Room, item, new(), TestGroupManager.Empty);
            Assert.NotNull(addon);
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(addon, new() { IntParams = [0, 0, 0, 1, 0, 100, 0], SelectedItems = [1] }, out var config, out var error), error);
            addon.ApplyConfiguration(config);
            Items[item.Id] = item;
            Assert.True(Engine.Add(addon));
        }
        public void Dispose()
        {
            Engine.Clear();
            typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, _originalGame);
        }
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
    public void ClockStopResetResumeAndRemovalHaveActualLifecycleState()
    {
        var item = MakeItem(1, "wf_upcounter1");
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        clocks.Control(item, 0, 0);
        clocks.Poll(500);
        clocks.Control(item, 1, 500);
        Assert.Empty(clocks.Poll(5000));
        Assert.Equal(500, clocks.ReadMilliseconds(item));
        clocks.Control(item, 4, 5000);
        Assert.True(clocks.IsRunning(item));
        Assert.Equal(500, clocks.ReadMilliseconds(item));
        clocks.TakeChanges();
        Assert.Equal(1000, Assert.Single(clocks.Poll(5500)).Event.Value);
        clocks.Control(item, 2, 5500);
        Assert.False(clocks.IsRunning(item));
        clocks.Forget(item);
        Assert.Null(clocks.ReadMilliseconds(item));
        Assert.Empty(clocks.Poll(10000));
        Assert.False(clocks.Control(item, 0, 10000));
    }

    [Fact]
    public void GameCounterCountsUpAndSupportsWiredClockControlsWithoutStartingAGame()
    {
        var item = MakeItem(1, "wf_game_upcounter1");
        item.LegacyDataString = "2";
        var clocks = new WiredCounterController();
        clocks.Attach(item);
        Assert.True(clocks.Use(item, 0, 0));
        Assert.Empty(clocks.TakeChanges());
        Assert.True(clocks.Control(item, 0, 0));
        Assert.Empty(clocks.Poll(499));
        Assert.Equal(2500, Assert.Single(clocks.Poll(500)).Event.Value);
        Assert.Equal("3", Assert.Single(clocks.Poll(1000)).Item.LegacyDataString);
        Assert.True(clocks.IsRunning(item));
        Assert.True(clocks.Adjust(item, 0, 0, 2));
        Assert.Equal(4000, clocks.ReadMilliseconds(item));
        clocks.TakeChanges();
        clocks.Use(item, 2, 1000);
        Assert.Equal("0", item.LegacyDataString);
        Assert.Equal(WiredEventKind.Counter, Assert.Single(clocks.TakeChanges()).Event.Kind);
        Assert.False(clocks.IsRunning(item));
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
        var user = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        user.SetStatus("sit");
        var context = Context(room, new(WiredEventKind.ClickUser) { TargetUser = user }, [], [user]);
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_user_performs_action"), Descriptor("wf_cnd_user_performs_action"),
            TestGroupManager.Empty, _ => null, () => DateTimeOffset.UtcNow);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [6, 0, 0, 0, 1, 11, 0] }, out var config, out _));
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
        var box = new WiredModernCondition(room, MakeItem(100, "wf_cnd_match_snapshot"), Descriptor("wf_cnd_match_snapshot"),
            TestGroupManager.Empty, _ => null, () => DateTimeOffset.UtcNow);
        var candidate = new WiredConfiguration { IntParams = [1, 1, 1, 1, 100, 0], SelectedItems = [1] };
        var prepared = WiredRoomOperations.PrepareSnapshots(box, candidate);
        Assert.Equal("state,with;delimiters", Assert.Single(prepared.Snapshots).State);
        Assert.Empty(candidate.Snapshots);
        Assert.Empty(box.Configuration.Snapshots);
    }

    [Fact]
    public void TriggerSeparatesUseAndStateMutationAndUsesStoredSnapshot()
    {
        var (room, _, items) = World();
        var item = MakeItem(1, "test");
        items[1] = item;
        var triggerItem = MakeItem(100, "wf_trg_state_changed");
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(triggerItem, room);
        var box = new WiredModernTrigger(room, triggerItem, Descriptor("wf_trg_state_changed"));
        WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [1, 100], SelectedItems = [1], Snapshots = [WiredRoomOperations.Capture(item)] }, out var config, out _);
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
        WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [10] }, out var config, out _);
        box.ApplyConfiguration(config);
        box.Reset(1000);
        Assert.Null(box.Poll(5999));
        Assert.Equal(WiredEventKind.Elapsed, box.Poll(6000)!.Kind);
        Assert.Null(box.Poll(11000));
        box.Reset(11000);
        Assert.NotNull(box.Poll(16000));
    }


    [Fact]
    public void RoomTimerResetStartsEveryTimerAtTheSameInstant()
    {
        var (room, _, items) = World();
        long now = 0;
        // Every reading moves the clock on, as a real one does between two calls.
        var engine = new WiredStackEngine(() => now++, box => items.TryGetValue(box.Item.Id, out var item) && ReferenceEquals(item, box.Item), _ => true, _ => { }, _ => { });
        var timers = Enumerable.Range(1, 3).Select(i =>
        {
            var item = MakeItem((uint)i, "wf_trg_at_given_time");
            item.SetState(i - 1, 0, 0, Gamemap.GetAffectedTiles(1, 1, i - 1, 0, 0));
            items[item.Id] = item;
            var box = new WiredModernTimedTrigger(room, item, Descriptor("wf_trg_at_given_time"));
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(box, new() { IntParams = [1] }, out var config, out _));
            box.ApplyConfiguration(config);
            Assert.True(engine.Add(box));

            return box;
        }).ToArray();
        now = 1000;

        engine.ResetTimers(items.Values.ToArray());

        var firstFires = timers.Select(timer => Enumerable.Range(1400, 200).First(t => timer.Poll(t) != null)).ToArray();
        Assert.Single(firstFires.Distinct());
    }

    [Fact]
    public void ResetTimersReachesTheWholeRoomWhateverTheFurniLimit()
    {
        var (room, _, _) = World();
        var action = ActionBox(room, "wf_act_reset_timers");
        WiredNativeTestSupport.InstallRuntime(action, WiredActionConfiguration.Defaults("wf_act_reset_timers"));
        Item[] furni = [MakeItem(1, "wf_trg_periodically"), MakeItem(2, "wf_trg_at_given_time"), MakeItem(3, "test")];
        var operations = new ResetOperations();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Use), new(() => furni, () => []), operations);
        context.Policy.Addons.FurniLimit = 1;

        Assert.True(action.Execute(context));

        Assert.Equal(furni.Select(item => item.Id), operations.Targets.Select(item => item.Id).Order());
    }

    [Fact]
    public void FullPlacementHonoursScopedUsersAndPreservesOrdinaryRejection()
    {
        var store = new RecordingPlacementStore();
        var (room, map, items) = World(store);
        var mover = MakeItem(1, "test");
        mover.SetState(0, 0, 0, Gamemap.GetAffectedTiles(1, 1, 0, 0, 0));
        items[1] = mover;
        map.AddToMap(mover);
        var occupant = new RoomUser(1, 0, 7, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 1, Y = 1 };
        map.AddUserToMap(occupant, new(1, 1));
        Assert.False(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false));
        Assert.Empty(store.FloorPlacements);
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: new(new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint>())));
        var allowed = new WiredCollisionPolicy(new HashSet<uint>(), new HashSet<int> { 7 }, new HashSet<uint>());
        Assert.True(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0, collision: allowed));
        Assert.Empty(store.FloorPlacements);
        Assert.True(room.GetRoomItemHandler().SetFloorItem(null!, mover, 1, 1, 0, false, false, false, wiredCollision: allowed));
        Assert.Equal(new Point(1, 1), new Point(mover.GetX, mover.GetY));
        Assert.DoesNotContain(mover, map.GetCoordinatedItems(new(0, 0)));
        Assert.Contains(mover, map.GetCoordinatedItems(new(1, 1)));
        Assert.Equal((mover.Id, room.Id, 1, 1, mover.GetZ, 0), Assert.Single(store.FloorPlacements));

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

    [Fact]
    public void AvatarMovementHonoursScopedThroughUsersAndExplicitBlockingItems()
    {
        using var fixture = new TeleportFixture();
        var room = fixture.Room;
        var actor = fixture.User;
        var occupant = new RoomUser(2, 0, 8, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        occupant.SetPos(1, 0, 0);
        RoomUsers(room)[8] = occupant;
        room.GetGameMap().AddUserToMap(occupant, new(1, 0));
        var action = ActionBox(room, "wf_act_move_rotate_user");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new() { IntParams = [2, -1, 0] }, out var config, out _));
        action.ApplyConfiguration(config);
        WiredRuntimeContext Firing()
        {
            var c = Context(room, new(WiredEventKind.Enter) { Actor = actor }, fixture.Items.Values.ToArray(), RoomUsers(room).Values.ToArray());
            c.Triggering.UserIds.Add(actor.VirtualId);
            c.Policy.Addons.DisableAnimation = true;

            return c;
        }
        Assert.False(action.Execute(Firing()));
        Assert.Equal(0, actor.X);
        var wrong = Firing();
        wrong.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 9 }, new HashSet<uint>());
        Assert.False(action.Execute(wrong));
        Assert.Equal(0, actor.X);
        var blocked = MakeItem(10, "test");
        blocked.SetState(1, 0, 0, Gamemap.GetAffectedTiles(1, 1, 1, 0, 0));
        fixture.Items[10] = blocked;
        room.GetGameMap().AddToMap(blocked);
        var context = Firing();
        context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint> { 10 });
        Assert.False(action.Execute(context));
        Assert.Equal(0, actor.X);
        context.Policy.Addons.Physics = new(false, new HashSet<uint>(), new HashSet<int> { 8 }, new HashSet<uint>());
        Assert.True(action.Execute(context));
        Assert.Equal(1, actor.X);
        Assert.Equal(0, actor.Y);
    }

    [Fact]
    public void OptionalUnnamedBotHandItemGrantsWithoutBotButExplicitMissingBotRejects()
    {
        using var fixture = new TeleportFixture();
        var action = ActionBox(fixture.Room, "wf_act_bot_give_handitem");
        var config = new WiredConfiguration { IntParams = [2, 0, 0], Text = "" };
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, config, out var valid, out _));
        action.ApplyConfiguration(valid);
        var context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = fixture.User }, fixture.Items.Values.ToArray(), [fixture.User]);
        context.Triggering.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context));
        Assert.Equal(2, fixture.User.CarryItemId);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, config with { IntParams = [3, 0, 100], Text = "missing" }, out valid, out _));
        action.ApplyConfiguration(valid);
        Assert.False(action.Execute(context));
        Assert.Equal(2, fixture.User.CarryItemId);
        var bot = Bot(fixture.Room, 8);
        RoomUsers(fixture.Room)[8] = bot;
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, config with { IntParams = [4, 200, 0] }, out valid, out _));
        action.ApplyConfiguration(valid);
        context = Context(fixture.Room, new(WiredEventKind.Enter) { Actor = bot }, fixture.Items.Values.ToArray(), [fixture.User, bot]);
        context.SelectorPool.UserIds.Add(fixture.User.VirtualId);
        Assert.True(action.Execute(context));
        Assert.Equal(4, fixture.User.CarryItemId);
    }

    [Fact]
    public void TemporaryPlacementMoveRemoveUsesRealMapsWithoutPersistence()
    {
        var (room, map, items) = World();
        var handler = room.GetRoomItemHandler();
        var definition = MakeItem(1, "test").Definition;
        definition.Stackable = true;
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => throw new InvalidOperationException("Temporary path opened SQL: " + method.Name);

        try {
            databaseField.SetValue(null, database);
            var item = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 0, "1"));
            Assert.True(item.IsTemporary);
            Assert.True(handler.OwnsTemporary(item));
            Assert.Equal(-1, unchecked((int)item.Id));
            Assert.Same(room, item.GetRoom());
            Assert.Same(item, items[item.Id]);
            Assert.Equal("1", item.LegacyDataString);
            Assert.Contains(item, map.GetCoordinatedItems(new(0, 0)));
            Assert.True(WiredRoomOperations.MoveItem(room, item, 1, 1, animate: false));
            Assert.DoesNotContain(item, map.GetCoordinatedItems(new(0, 0)));
            Assert.Contains(item, map.GetCoordinatedItems(new(1, 1)));
            Assert.True(handler.RemoveTemporaryFloorItem(item));
            Assert.False(handler.OwnsTemporary(item));
            Assert.False(items.ContainsKey(item.Id));
            Assert.DoesNotContain(item, map.GetCoordinatedItems(new(1, 1)));
            Assert.False(handler.RemoveTemporaryFloorItem(item));
            var replacement = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0));
            Assert.Equal(-2, unchecked((int)replacement.Id));
            Assert.False(handler.RemoveTemporaryFloorItem(MakeItem(replacement.Id, "test")));
            handler.UpdateItem(replacement);
            handler.Dispose(); // No temporary extra-data or coordinate SQL during unload.
        }
        finally {
            databaseField.SetValue(null, original);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void ActualPlacementReadiesFloorAndWallFxOnlyAfterSuccessfulAnnouncement(bool wall, bool enqueue)
    {
        using var f = new TeleportFixture();
        f.Habbo.Gender = "M";
        f.Habbo.Motto = "";
        f.Habbo.Look = "test";
        f.Habbo.HabboStats = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);
        f.Room.SendObjects(f.Client);
        Assert.Single(f.Room.GetWired().CaptureFxViewers());
        f.Client.SendCallback = _ => enqueue ? true : throw new IOException("placement enqueue failed");
        var databaseField = typeof(PlusEnvironment).GetField("_database", BindingFlags.Static | BindingFlags.NonPublic)!;
        var original = databaseField.GetValue(null);
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (method, _) => throw new NotSupportedException(method.Name);

        try {
            databaseField.SetValue(null, database);
            Item placed;

            if (wall) {
                placed = MakeItem(50, "wall");
                placed.Definition.Type = ItemType.Wall;
                placed.WallCoordinates = ":w=1,1 l=10,20 l";
                placed.Username = "Alice";
                typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(placed, f.Room);
                Assert.True(f.Room.GetRoomItemHandler().SetWallItem(f.Client, placed));
            }
            else {
                placed = Assert.IsType<Item>(f.Room.GetRoomItemHandler().PlaceTemporaryFloorItem(MakeItem(50, "floor").Definition, 1, 2, 2, 0));
            }

            Assert.Same(placed, f.Room.GetRoomItemHandler().GetItem(placed.Id));
            var ready = Assert.Single(f.Room.GetWired().CaptureFxViewers()).ReadyHolders;
            Assert.Equal(enqueue, ready.Contains(WiredVariableRuntimeFrames.FurniHolder(placed)));
        }
        finally {
            databaseField.SetValue(null, original);
        }
    }

    [Fact]
    public void TemporaryPlacementChecksFullFootprintHeightAndRoomLimit()
    {
        var (room, map, _) = World();
        var handler = room.GetRoomItemHandler();
        var wide = MakeItem(1, "test").Definition;
        wide.Length = 2;
        wide.Stackable = true;
        Assert.Null(handler.PlaceTemporaryFloorItem(wide, 1, 2, 2, 0));
        var footprint = Gamemap.GetAffectedTiles(wide.Length, wide.Width, 0, 0, 0).Values.First();
        map.Model.SqState[footprint.X, footprint.Y] = SquareState.Blocked;
        Assert.Null(handler.PlaceTemporaryFloorItem(wide, 1, 0, 0, 0, 0));
        map.Model.SqState[footprint.X, footprint.Y] = SquareState.Open;
        var definition = MakeItem(1, "test").Definition;
        definition.Stackable = true;
        definition.Height = 1;
        Assert.Null(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 80));
        definition.Height = 0;

        for (var i = 0; i < RoomItemHandling.TemporaryItemLimit; i++) {
            Assert.NotNull(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0, 0));
        }

        Assert.Null(handler.PlaceTemporaryFloorItem(definition, 1, 0, 0, 0));
        Assert.Equal(RoomItemHandling.TemporaryItemLimit, handler.GetFloor.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TemporaryRemovalEditorModesProtectPermanentAndMismatchedIdentities(int mode)
    {
        using var f = new TeleportFixture();
        var handler = f.Room.GetRoomItemHandler();
        var def = MakeItem(3, "test").Definition;
        def.Stackable = true;
        var permanent = MakeItem(uint.MaxValue, "test");
        f.Items[permanent.Id] = permanent;
        var item = Assert.IsType<Item>(handler.PlaceTemporaryFloorItem(def, 1, 2, 2, 0));
        Assert.Equal(-2, unchecked((int)item.Id)); // A real permanent high-uint item owns the -1 bit pattern.
        var action = ActionBox(f.Room, "wf_act_remove_furni");
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new() { IntParams = [mode, 0] }, out var config, out _));
        action.ApplyConfiguration(config);
        var ctx = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
        ctx.Triggering.FurniIds.UnionWith([permanent.Id, item.Id]);
        Assert.True(action.Execute(ctx));
        Assert.Same(permanent, handler.GetItem(permanent.Id));
        Assert.Null(handler.GetItem(item.Id));
        Assert.False(action.Execute(ctx));
    }

    [Theory]
    [InlineData(true)]
    public void RoomOwnedTemporaryPlacementUsesInjectedDefinitionsWithGlobalGameUnavailable(bool snapshot)
    {
        using var fixture = new TeleportFixture();
        var definition = MakeItem(55, "injected-definition").Definition;
        definition.Id = 55;
        definition.Stackable = true;
        var reads = 0;
        fixture.DefinitionManager = new TestWiredDefinitions(() =>
        {
            reads++;

            return new Dictionary<uint, ItemDefinition> { [55] = definition };
        });
        var action = Assert.IsType<WiredModernAction>(fixture.Room.GetWired().CreateConfiguredBox(
            MakeItem(102, "wf_act_place_furni"), Descriptor("wf_act_place_furni")));
        Assert.Equal(0, reads);
        var proposed = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with
        {
            IntParams = [55, 2, 1, 2, 2, 0]
        };

        if (snapshot) {
            proposed = proposed with
            {
                TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude),
                Snapshots = [new(900, 55, 2, 2, 1, 0, "1"), new(901, 999, 1, 2, 1, 0, "0")]
            };
        }

        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, proposed, out var configuration, out var error), error);
        action.ApplyConfiguration(configuration);
        var global = typeof(PlusEnvironment).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = global.GetValue(null);

        try {
            global.SetValue(null, null);
            Assert.True(action.Execute(Context(fixture.Room, new(WiredEventKind.Use), fixture.Items.Values.ToArray(), [fixture.User])));
        }
        finally {
            global.SetValue(null, previous);
        }

        var placed = fixture.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray();
        Assert.Equal(snapshot ? 1 : 2, placed.Length);
        Assert.All(placed, item =>
        {
            Assert.Same(definition, item.Definition);
            Assert.Equal((2, 2), (item.GetX, item.GetY));
        });
        Assert.Equal(snapshot ? 2 : 1, reads);

        if (snapshot) {
            Assert.Equal("1", Assert.Single(placed).LegacyDataString);
        }
    }

    [Fact]
    public void SnapshotPlacementCopiesDetachedTemplatesWithRelativeGeometryAndAltitude()
    {
        using var f = new TeleportFixture();
        var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        var def = MakeItem(5, "test").Definition;
        def.Id = 5;
        def.Stackable = true;
        ((RecordingProxy)(object)manager).InvokeMethod = (m, _) => m.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = def } : null;
        f.DefinitionManager = manager;
        f.Target.Definition.Stackable = true;
        var action = ActionBox(f.Room, "wf_act_place_furni", definitions: f.DefinitionManager);
        var proposed = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with
        {
            TemporaryPlacement = new(Location: WiredPlaceLocationType.CustomLocation, Altitude: WiredPlaceAltitudeType.CustomAltitude, OffsetAltitudeHundredths: 125),
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("target", WiredSources.Snapshot),
            SecondarySelectedItems = [1],
            Snapshots = [new(900, 5, 7, 7, 3, 0, "1"), new(901, 5, 8, 7, 4, 2, "0")]
        };
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, proposed, out var config, out _));
        action.ApplyConfiguration(config);
        var context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
        Assert.True(action.Execute(context));
        var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).OrderBy(item => item.GetX).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Equal((1, 1, 1.25, "1"), (copies[0].GetX, copies[0].GetY, copies[0].GetZ, copies[0].LegacyDataString));
        Assert.Equal((2, 1, 1.25, 2), (copies[1].GetX, copies[1].GetY, copies[1].GetZ, copies[1].Rotation));
        var prepared = WiredRoomOperations.PrepareSnapshots(action, proposed with { SelectedItems = [copies[0].Id], SecondarySelectedItems = [copies[1].Id] });
        Assert.Empty(prepared.SelectedItems);
        Assert.Empty(prepared.SecondarySelectedItems);
        var template = Assert.Single(prepared.Snapshots);
        Assert.Equal(5u, template.DefinitionId);
        Assert.Equal("1", template.State);
        Assert.Equal(prepared.Snapshots, WiredRoomOperations.PrepareSnapshots(action, prepared).Snapshots);
        Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[0]));
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, prepared with { TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude) }, out config, out _));
        action.ApplyConfiguration(config);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
    }

    [Fact]
    public void DynamicPlaceSourcesCaptureCurrentFloorTemplatesAndLeaveSavedSnapshotsFrozen()
    {
        var (room, _, _) = World();
        var definition = MakeItem(5, "source").Definition;
        definition.Id = 5;
        definition.Stackable = true;
        var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        ((RecordingProxy)(object)manager).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = definition } : null;
        var source = MakeItem(8, "source");
        source.Definition = definition;
        source.LegacyDataString = "3";
        source.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0));
        var transient = Assert.IsType<Item>(room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 1, 0, 1, 0));
        var box = MakeItem(100, "wf_act_place_furni");
        var config = new WiredConfiguration
        {
            TemporaryPlacement = new(),
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Trigger)
        };
        var present = new[] { source, transient };

        var trigger = Context(room, new(WiredEventKind.Use), present, []);
        trigger.Triggering.FurniIds.UnionWith([source.Id, transient.Id]);
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, trigger, config, manager));
        var copy = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id));
        Assert.Equal((2, 1, 0, "3"), (copy.GetX, copy.GetY, copy.Rotation, copy.LegacyDataString));
        Assert.Same(definition, copy.Definition);
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(copy));

        var selector = Context(room, new(WiredEventKind.Use), present, []);
        selector.SelectorPool.FurniIds.Add(source.Id);
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, selector, config with
        {
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Selector)
        }, manager));
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id))));

        var signal = Context(room, new(WiredEventKind.Use), present, []);
        signal.Signal = new(new([source.Id]), new Dictionary<string, long>());
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, signal, config with
        {
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Signal)
        }, manager));
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id))));

        var frozen = Context(room, new(WiredEventKind.Use), present, []);
        frozen.Triggering.FurniIds.Add(source.Id);
        Assert.False(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, frozen, config with
        {
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Selected)
        }, manager));
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, frozen, config with
        {
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Selected),
            Snapshots = [new(900, 5, 0, 0, 0, 0, "1")]
        }, manager));
        var saved = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id));
        Assert.Equal((0, 0, "1"), (saved.GetX, saved.GetY, saved.LegacyDataString));
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(saved));

        var primary = MakeItem(11, "anchor");
        primary.SetState(1, 0, 0, Gamemap.GetAffectedTiles(1, 1, 1, 0, 0));
        var secondaryAnchor = MakeItem(12, "anchor");
        secondaryAnchor.SetState(0, 2, 0, Gamemap.GetAffectedTiles(1, 1, 0, 2, 0));
        var aimed = Context(room, new(WiredEventKind.Use), [primary, secondaryAnchor], []);
        var aimedConfig = new WiredConfiguration
        {
            TemporaryPlacement = new(Location: WiredPlaceLocationType.CustomLocation),
            SelectedItems = [primary.Id],
            SecondarySelectedItems = [secondaryAnchor.Id],
            Snapshots = [new(900, 5, 0, 0, 0, 0, "1")],
            FurniSources = ImmutableDictionary<string, int>.Empty
                .Add("templates", WiredSources.Selected)
                .Add("target", WiredSources.Selected)
        };
        WiredConfiguration Bind(WiredConfiguration draft)
        {
            var descriptor = WiredBoxRegistry.All.Single(entry => entry.CanonicalName == "wf_act_place_furni");
            var native = WiredNativeTestSupport.FromRuntime(descriptor, draft);
            Assert.True(WiredNativeEditorProjection.TryCompile(box.Id, descriptor, native, out var compiled));
            return compiled;
        }
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, aimed, Bind(aimedConfig), manager));
        var onPrimary = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id));
        Assert.Equal((1, 0), (onPrimary.GetX, onPrimary.GetY));
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(onPrimary));
        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, aimed, Bind(aimedConfig with
        {
            FurniSources = aimedConfig.FurniSources.SetItem("target", WiredSources.Snapshot)
        }), manager));
        var onSecondary = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary && item.Id != transient.Id));
        Assert.Equal((0, 2), (onSecondary.GetX, onSecondary.GetY));
    }

    [Fact]
    public void DynamicPlaceSpawnResolvesOpaqueCatalogIdsAndLeavesLiveTokensUnread()
    {
        var (room, _, _) = World();
        room.Id = 1;
        var definition = MakeItem(5, "source").Definition;
        definition.Id = 5;
        definition.Stackable = true;
        var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        ((RecordingProxy)(object)manager).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = definition } : null;
        var source = MakeItem(8, "source");
        source.Definition = definition;
        source.LegacyDataString = "3";
        source.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0));
        var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance,
            TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance,
            TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, wired);
        var module = new WiredVariableModule(1, new PlaceCatalogDirectory(), new MemoryWiredVariableStore(), TimeProvider.System);
        typeof(WiredRoomVariables).GetField("<Module>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room.GetWired().Variables, module);
        var frame = new WiredVariableFrame(1, []);
        Assert.True(module.Mutate(new(WiredVariableTarget.Context, "custom:11"), new(WiredVariableTarget.Context, 0, 0), WiredVariableMutation.Give, 42, frame));
        var box = MakeItem(100, "wf_act_place_furni");
        var config = new WiredConfiguration
        {
            TemporaryPlacement = new(SpawnWithVariable: true, ValueIsVariable: true, ValueTarget: (int)WiredVariableTarget.Context),
            FurniSources = ImmutableDictionary<string, int>.Empty.Add("templates", WiredSources.Trigger),
            VariableIds = ["furni:10", "ctx:11"]
        };
        var context = Context(room, new(WiredEventKind.Use), [source], []);
        context.Triggering.FurniIds.Add(source.Id);
        context.VariableFrame = frame;

        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, context, config, manager));
        var copy = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
        Assert.Equal((2, 1, "3"), (copy.GetX, copy.GetY, copy.LegacyDataString));
        var holder = WiredVariableRuntimeFrames.FurniHolder(copy);
        var read = new WiredVariableFrame(1, [holder]);
        Assert.Equal(42, module.Read(new(WiredVariableTarget.Furni, "custom:10"), holder, read)!.Value);
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(copy));

        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, context, config with { VariableIds = ["furni:10", "custom:11"] }, manager));
        copy = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
        holder = WiredVariableRuntimeFrames.FurniHolder(copy);
        Assert.Equal(0, module.Read(new(WiredVariableTarget.Furni, "custom:10"), holder, new(1, [holder]))!.Value);
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(copy));

        Assert.True(WiredTemporaryFurnitureActions.Execute("wf_act_place_furni", box, context, config with { VariableIds = ["custom:10", "ctx:11"] }, manager));
        copy = Assert.Single(room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
        Assert.Null(module.Read(new(WiredVariableTarget.Furni, "custom:10"), WiredVariableRuntimeFrames.FurniHolder(copy), new(1, [WiredVariableRuntimeFrames.FurniHolder(copy)])));
    }

    [Fact]
    public void SnapshotPreparationPreservesSelectedPivotOrderAfterOriginalsDetach()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Stackable = true;
        var definition = MakeItem(5, "test").Definition;
        definition.Id = 5;
        definition.Stackable = true;
        var manager = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
        ((RecordingProxy)(object)manager).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [5] = definition } : null;
        f.DefinitionManager = manager;
        var firstInserted = MakeItem(10, "test");
        firstInserted.Definition = definition;
        firstInserted.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        f.Items[10] = firstInserted;
        var firstPicked = MakeItem(20, "test");
        firstPicked.Definition = definition;
        firstPicked.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0));
        f.Items[20] = firstPicked;
        var action = ActionBox(f.Room, "wf_act_place_furni", definitions: f.DefinitionManager);
        var proposed = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with { SelectedItems = [20, 10], SecondarySelectedItems = [1], FurniSources = ImmutableDictionary<string, int>.Empty.Add("target", 101), TemporaryPlacement = new(Location: WiredPlaceLocationType.CustomLocation) };
        var captured = WiredRoomOperations.PrepareSnapshots(action, proposed);
        Assert.Equal(new uint[] { 20, 10 }, captured.Snapshots.Select(snapshot => snapshot.ItemId));
        f.Items.TryRemove(10, out _);
        f.Items.TryRemove(20, out _);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, captured, out var config, out _));
        action.ApplyConfiguration(config);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
        var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.Contains(copies, item => item.GetX == 1 && item.GetY == 1);
        Assert.Contains(copies, item => item.GetX == 0 && item.GetY == 1);
        Assert.DoesNotContain(copies, item => item.GetX == 2);
    }

    [Fact]
    public void NativePlaceSkipsWallTemplatesBeforeChoosingTheFloorAnchor()
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Stackable = true;
        var floor = MakeItem(10, "floor-template");
        floor.Definition.Id = 5;
        floor.Definition.Stackable = true;
        floor.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        f.Items[floor.Id] = floor;
        var wall = MakeItem(20, "wall-template");
        wall.Definition.Id = 6;
        wall.Definition.Type = ItemType.Wall;
        wall.WallCoordinates = ":w=0,0 l=0,0 l";
        ((ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_wallItems", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(f.Room.GetRoomItemHandler())!)[wall.Id] = wall;
        var definitions = new TestWiredDefinitions(() => new() { [5] = floor.Definition, [6] = wall.Definition });
        var action = ActionBox(f.Room, "wf_act_place_furni", definitions: definitions);
        var native = WiredNativeEditorProjection.DefaultNative(action.Descriptor) with
        {
            OwnedIntParams = [0, 1, 0, 0, 0, 0, 0, 0, 0, 0],
            PrimaryItems = [new(wall.Id, true), new(floor.Id, false)],
            SecondaryItems = [new(f.Target.Id, false)],
            FurniSourceTypes = [100, 101, 100]
        };
        var snapshots = (ImmutableArray<WiredFurniSnapshot>)typeof(WiredConfigurationService)
            .GetMethod("CaptureSnapshots", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [f.Room, "wf_act_place_furni", native])!;
        Assert.Equal(floor.Id, Assert.Single(snapshots).ItemId);
        // The runtime also filters admitted native snapshot records before choosing the pivot.
        native = native with { SavedState = new() { Snapshots = [WiredRoomOperations.Capture(wall), .. snapshots] } };
        Assert.True(WiredNativeEditorProjection.TryCompile(action.Item.Id, action.Descriptor, native, out var compiled));
        Assert.True(WiredConfigurationSave.TrySave(action, compiled, TestWiredConfigurationStore.Instance, out var error), error);
        Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
        var copy = Assert.Single(f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
        Assert.Equal((f.Target.GetX, f.Target.GetY), (copy.GetX, copy.GetY));
    }

    [Fact]
    public void RewardValidationRejectsUnsupportedCurrenciesAndKeepsActualIntervals()
    {
        var config = WiredRewards.Defaults() with { Text = "1,furni#5,100;0,ABC,50" };
        Assert.True(WiredRewards.TryValidate(config, out _, out _));

        foreach (var code in new[] { "credits#5", "pixels#5", "diamonds#5", "points5#5" }) {
            Assert.False(WiredRewards.TryValidate(config with { Text = "1," + code + ",100" }, out _, out _));
        }

        Assert.False(WiredRewards.TryValidate(config with { Text = string.Join(';', Enumerable.Repeat("0,A,100", 21)) }, out _, out _));
        var claim = new WiredRewardClaim { Count = 1, LastClaimAt = DateTimeOffset.FromUnixTimeSeconds(100) };
        Assert.False(WiredRewards.IntervalOpen(claim, 0, 1, DateTimeOffset.MaxValue));

        foreach (var pair in new[] { (1, 86400), (2, 3600), (3, 60) }) {
            Assert.False(WiredRewards.IntervalOpen(claim, pair.Item1, 2, DateTimeOffset.FromUnixTimeSeconds(100 + 2 * pair.Item2 - 1)));
            Assert.True(WiredRewards.IntervalOpen(claim, pair.Item1, 2, DateTimeOffset.FromUnixTimeSeconds(100 + 2 * pair.Item2)));
        }
    }

    [Fact]
    public void RewardActionDatabaseFailureEmitsNoInventoryOrResultPacket()
    {
        using var f = new TeleportFixture();
        f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
        var sent = 0;
        f.Client.SendCallback = _ => { sent++; return true; };
        var database = DispatchProxy.Create<IDatabase, RecordingProxy>();
        ((RecordingProxy)(object)database).InvokeMethod = (_, _) => throw new InvalidOperationException("Injected SQL failure");
        var rewards = new WiredRewardService(new WiredRewardStore(database), DispatchProxy.Create<IItemDataManager, RecordingProxy>(), TimeProvider.System, TestLogging.Rewards);
        var action = ActionBox(f.Room, "wf_act_give_reward", rewards: rewards);
        Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, WiredRewards.Defaults() with { Text = "1,furni#5,100" }, out var config, out _));
        action.ApplyConfiguration(config);
        var ctx = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, f.Items.Values.ToArray(), [f.User]);
        ctx.Triggering.UserIds.Add(f.User.VirtualId);
        Assert.False(action.Execute(ctx));
        Assert.Equal(0, sent);
        Assert.Empty(f.Habbo.Inventory.Furniture.GetItems);
    }

    [Fact]
    public void RewardServiceReadsOneInjectedClockAndPublishesOnlyAfterStoreCommit()
    {
        using var f = new TeleportFixture();
        f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
        var sent = 0;
        f.Client.SendCallback = _ => { sent++; return true; };
        var clock = new RewardClock(DateTimeOffset.FromUnixTimeSeconds(1234));
        var store = new RecordingRewardStore(() => sent, new(4, "BADGE1"));
        var rewards = new WiredRewardService(store, DispatchProxy.Create<IItemDataManager, RecordingProxy>(), clock, TestLogging.Rewards);
        var ctx = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, f.Items.Values.ToArray(), [f.User]);
        ctx.Triggering.UserIds.Add(f.User.VirtualId);
        Assert.True(WiredRewards.TryValidate(WiredRewards.Defaults() with { Text = "0,BADGE1,100" }, out var config, out _));
        Assert.True(rewards.Execute(MakeItem(100, "wf_act_give_reward"), ctx, config));
        Assert.Equal(new[] { DateTimeOffset.FromUnixTimeSeconds(1234) }, store.Times);
        Assert.Equal(1, clock.Reads);
        Assert.Equal(new[] { 0 }, store.SentAtClaim); // Nothing reaches the client before the store commits.
        Assert.True(sent > 0);
        Assert.True(f.Habbo.Inventory.Badges.HasBadge("BADGE1"));
    }

    [Fact]
    public void RewardServiceStoreFailureLogsAndPublishesNothing()
    {
        using var f = new TeleportFixture();
        f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
        var sent = 0;
        f.Client.SendCallback = _ => { sent++; return true; };
        var clock = new RewardClock(DateTimeOffset.FromUnixTimeSeconds(1234));
        var store = new RecordingRewardStore(() => sent, null, new InvalidOperationException("Injected commit failure"));
        var rewards = new WiredRewardService(store, DispatchProxy.Create<IItemDataManager, RecordingProxy>(), clock, TestLogging.Rewards);
        var ctx = Context(f.Room, new(WiredEventKind.Enter) { Actor = f.User }, f.Items.Values.ToArray(), [f.User]);
        ctx.Triggering.UserIds.Add(f.User.VirtualId);
        Assert.True(WiredRewards.TryValidate(WiredRewards.Defaults() with { Text = "0,BADGE1,100" }, out var config, out _));
        Assert.False(rewards.Execute(MakeItem(100, "wf_act_give_reward"), ctx, config));
        Assert.Equal(new[] { DateTimeOffset.FromUnixTimeSeconds(1234) }, store.Times);
        Assert.Equal(0, sent);
        Assert.False(f.Habbo.Inventory.Badges.HasBadge("BADGE1"));
    }

    private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    private sealed class RewardClock(DateTimeOffset now) : TimeProvider
    {
        public int Reads { get; private set; }
        public override DateTimeOffset GetUtcNow()
        {
            Reads++;

            return now;
        }
    }

    private sealed class RecordingRewardStore(Func<int> sent, WiredRewardGrant? grant, Exception? failure = null) : IWiredRewardStore
    {
        public List<DateTimeOffset> Times { get; } = [];
        public List<int> SentAtClaim { get; } = [];
        public WiredRewardGrant ClaimAndGrant(Item box, uint roomId, Habbo habbo, WiredConfiguration configuration,
            IItemDataManager definitions, DateTimeOffset now)
        {
            Times.Add(now);
            SentAtClaim.Add(sent());

            if (failure != null) {
                throw failure;
            }

            return grant!;
        }
    }

    [WiredVariableDatabaseFact]
    public async Task ActualRewardSqlSerializesQuotaGrantAndRollbackBeforePublication()
    {
        using var f = new TeleportFixture();
        var connectionString = ModernWiredDatabaseProbe.GuardedConnectionString();
        using var admin = new MySqlConnection(connectionString);
        await admin.OpenAsync();
        Assert.Equal("InnoDB", admin.QuerySingle<string>("SELECT ENGINE FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name='wired_reward_state'"));
        var userId = 0u;
        var roomId = 0u;
        var boxId = 0u;
        var badgeCode = "WR" + Guid.NewGuid().ToString("N")[..10];

        try {
            userId = ModernWiredDatabaseProbe.Insert(admin, "users", new() { ["username"] = badgeCode, ["password"] = "unused", ["mail"] = badgeCode + "@invalid" });
            roomId = ModernWiredDatabaseProbe.Insert(admin, "rooms", new() { ["owner"] = userId.ToString(), ["caption"] = "Disposable atomic reward probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
            var baseId = admin.QueryFirst<uint>("SELECT id FROM furniture WHERE type='s' LIMIT 1");
            boxId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var loadedRows = new DataTable();

            using (var reader = admin.ExecuteReader("SELECT items.*,users.username FROM items JOIN users ON users.id=items.user_id WHERE items.id=@boxId", new { boxId })) {
                loadedRows.Load(reader);
            }

            var box = ItemLoader.ReadRoomItem(Assert.Single(loadedRows.Rows.Cast<DataRow>()), roomId, MakeItem(boxId, "wf_act_give_reward").Definition);
            f.Habbo.Id = checked((int)userId);
            f.Habbo.Inventory = new() { Furniture = new([], []), Badges = new(new()) };
            var sent = 0;
            f.Client.SendCallback = _ => { sent++; return true; };
            var definition = MakeItem(baseId, "probe_product").Definition;
            definition.Id = baseId;
            var definitions = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
            ((RecordingProxy)(object)definitions).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [baseId] = definition } : method.Name == "GetItemByName" ? definition : null;
            var database = new ModernWiredDatabaseProbe.ProbeDatabase(connectionString);
            var store = new WiredRewardStore(database);
            var config = WiredRewards.Defaults() with { Text = $"1,furni#{baseId},100" };
            var loadedOwner = box.OwnerId;
            box.OwnerId = userId + 1;
            Assert.Equal(8, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(100)).Reason);
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            box.OwnerId = loadedOwner;
            var grant = store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(100));
            Assert.Equal(5, grant.Reason);
            Assert.Equal(0, sent);
            Assert.Empty(f.Habbo.Inventory.Furniture.GetItems);
            Assert.Equal(1, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE id=@id AND user_id=@user AND room_id=0", new { id = grant.Furniture!.Id, user = userId }));
            WiredRewards.Publish(f.Habbo, grant);
            Assert.True(sent > 0);
            Assert.Single(f.Habbo.Inventory.Furniture.GetItems);
            Assert.Equal(2, new WiredRewardStore(database).ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(200)).Reason);
            config = config with { IntParams = [3, 0, 0, 1, 0] };
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(159)).Reason);
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(160)).Reason);
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            config = config with { IntParams = [3, 0, 1, 1, 0] };
            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => new WiredRewardStore(database).ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(1000))));
            var results = await Task.WhenAll(tasks);
            Assert.Single(results, result => result.Reason == 5);
            Assert.Equal(3, results.Count(result => result.Reason == 1));
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            var unique = config with { IntParams = [3, 1, 0, 1, 0], Text = $"1,furni#{baseId},100;1,probe_product,100" };
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, At(1000)).Reason);
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, At(1060)).Reason);
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, At(1120)).Reason);
            var state = System.Text.Json.JsonSerializer.Deserialize<Dictionary<int, WiredRewardClaim>>(admin.QuerySingle<string>("SELECT claims FROM wired_reward_state WHERE item_id=@boxId", new { boxId }))!;
            Assert.Equal(2, state[f.Habbo.Id].Count);
            Assert.Contains("probe_product", state[f.Habbo.Id].ReceivedCodes);
            Assert.Contains($"furni#{baseId}", state[f.Habbo.Id].ReceivedCodes);
            unique = unique with { Text = "1,new_product_after_edit,100" };
            Assert.Equal(5, store.ClaimAndGrant(box, roomId, f.Habbo, unique, definitions, At(1120)).Reason); // Existing claims survive edits; new codes become available.
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            var before = admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE user_id=@user", new { user = userId });
            database.FailSqlPrefix = "UPDATE wired_reward_state";
            Assert.Throws<ModernWiredDatabaseProbe.InjectedCommandFailure>(() => store.ClaimAndGrant(box, roomId, f.Habbo, config, definitions, At(2000)));
            Assert.Equal(before, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM items WHERE user_id=@user", new { user = userId }));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            database.FailSqlPrefix = null;
            admin.Execute("INSERT INTO badge_definitions(code,required_right) VALUES (@badgeCode,'')", new { badgeCode });
            var badgeConfig = WiredRewards.Defaults() with { Text = "0," + badgeCode + ",100" };
            database.FailSqlPrefix = "UPDATE wired_reward_state";
            Assert.Throws<ModernWiredDatabaseProbe.InjectedCommandFailure>(() => store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, At(2000)));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM user_badges WHERE user_id=@userId", new { userId }));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
            database.FailSqlPrefix = null;
            var badgeGrant = store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, At(2000));
            Assert.Equal(4, badgeGrant.Reason);
            Assert.False(f.Habbo.Inventory.Badges.HasBadge(badgeCode));
            WiredRewards.Publish(f.Habbo, badgeGrant);
            Assert.True(f.Habbo.Inventory.Badges.HasBadge(badgeCode));
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            Assert.Equal(2, store.ClaimAndGrant(box, roomId, f.Habbo, badgeConfig, definitions, At(2100)).Reason);
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_reward_state WHERE item_id=@boxId", new { boxId }));
        }
        finally {
            admin.Execute("DELETE FROM wired_reward_state WHERE item_id=@boxId", new { boxId });
            admin.Execute("DELETE FROM user_badges WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM badge_definitions WHERE code=@badgeCode", new { badgeCode });
            admin.Execute("DELETE FROM items WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM rooms WHERE id=@roomId", new { roomId });
            admin.Execute("DELETE FROM users WHERE id=@userId", new { userId });
        }
    }

    [WiredVariableDatabaseFact]
    public void ActualSnapshotSpawnGivesTwoEphemeralVariablesAndDetachesWithoutDurableValues()
    {
        var connectionString = ModernWiredDatabaseProbe.GuardedConnectionString();
        using var f = new TeleportFixture(database: new ModernWiredDatabaseProbe.ProbeDatabase(connectionString));
        using var admin = new MySqlConnection(connectionString);
        admin.Open();
        var userId = 0u;
        var roomId = 0u;
        var variableId = 0u;
        var operandId = 0u;

        try {
            var suffix = "WT" + Guid.NewGuid().ToString("N")[..10];
            userId = ModernWiredDatabaseProbe.Insert(admin, "users", new() { ["username"] = suffix, ["password"] = "unused", ["mail"] = suffix + "@invalid" });
            roomId = ModernWiredDatabaseProbe.Insert(admin, "rooms", new() { ["owner"] = userId.ToString(), ["caption"] = "Disposable temporary variable probe", ["model_name"] = admin.QueryFirst<string>("SELECT id FROM room_models LIMIT 1") });
            var baseId = admin.QueryFirst<uint>("SELECT id FROM furniture WHERE type='s' LIMIT 1");
            variableId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var variable = WiredNativeEditorProjection.DefaultNative(Descriptor("wf_var_furni")) with { OwnedIntParams = [1, 1], Text = "spawnvalue" };
            admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@variableId,'wf_var_furni',2,@config)", new { variableId, config = System.Text.Json.JsonSerializer.Serialize(variable) });
            f.Room.Id = roomId;
            f.Room.OwnerId = (int)userId;
            f.Target.Definition.Stackable = true;
            var definition = MakeItem(baseId, "probe").Definition;
            definition.Id = baseId;
            definition.Stackable = true;
            var definitions = DispatchProxy.Create<IItemDataManager, RecordingProxy>();
            ((RecordingProxy)(object)definitions).InvokeMethod = (method, _) => method.Name == "get_Items" ? new Dictionary<uint, ItemDefinition> { [baseId] = definition } : null;
            f.DefinitionManager = definitions;
            var spawnId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var loadedRows = new DataTable();

            using (var reader = admin.ExecuteReader("SELECT items.*,users.username FROM items JOIN users ON users.id=items.user_id WHERE items.id=@spawnId", new { spawnId })) {
                loadedRows.Load(reader);
            }

            var action = ActionBox(f.Room, "wf_act_place_furni", definitions: f.DefinitionManager);
            action.Item = ItemLoader.ReadRoomItem(Assert.Single(loadedRows.Rows.Cast<DataRow>()), roomId, action.Item.Definition);
            typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(action.Item, f.Room);
            f.Items[spawnId] = action.Item;
            var removals = new List<byte[]>();
            f.Client.SendCallback = args =>
            {
                var packet = args.MemoryBuffer.Span.Slice(args.Offset, args.Count).ToArray();

                if (FlashGameClient.DecodeInt16(packet.AsMemory(4, 2)) == ServerPacketHeader.ObjectRemoveComposer) {
                    removals.Add(packet);
                }

                return true;
            };
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, new()
            {
                TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude),
                Snapshots = [new(0, baseId, 1, 1, 0, 2, "0")]
            }, out var raw, out _));
            action.ApplyConfiguration(raw);
            Assert.True(action.Execute(Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User])));
            var literal = Assert.Single(f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary));
            Assert.Equal(userId, literal.OwnerId);
            Assert.Equal((int)userId, literal.UserId);
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(literal));
            var removal = new FlashIncomingPacket { Buffer = Assert.Single(removals).AsMemory(6) };
            Assert.Equal(unchecked((int)literal.Id).ToString(), removal.ReadString());
            Assert.False(removal.ReadBool());
            Assert.Equal((int)userId, removal.ReadInt());
            Assert.Equal(0, removal.ReadInt());
            Assert.False(removal.HasDataRemaining());
            var config = WiredTemporaryFurnitureActions.Defaults("wf_act_place_furni") with
            {
                TemporaryPlacement = new(Altitude: WiredPlaceAltitudeType.SourceAltitude, SpawnWithVariable: true, Value: 37),
                VariableIds = [$"furni:{variableId}", "n"],
                Snapshots = [new(0, baseId, 1, 1, 0, 0, "1"), new(0, baseId, 2, 1, 0, 0, "0")]
            };
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, config, out config, out _));
            action.ApplyConfiguration(config);
            var context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
            context.VariableFrame = new(roomId, []);
            Assert.True(action.Execute(context));
            var copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray();
            Assert.Equal(2, copies.Length);
            Assert.All(copies, item => { Assert.Equal(userId, item.OwnerId); Assert.Equal((int)userId, item.UserId); });
            var holders = copies.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray();
            Assert.NotEqual(holders[0].StorageId, holders[1].StorageId);
            var frame = new WiredVariableFrame(roomId, holders);
            var module = f.Room.GetWired().Variables.Module;
            var reference = new WiredVariableReference(WiredVariableTarget.Furni, $"custom:{variableId}");
            Assert.All(holders, holder => Assert.Equal(37, module.Read(reference, holder, frame)!.Value));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@variableId", new { variableId }));
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[0]));
            Assert.Null(module.Read(reference, holders[0], frame));
            Assert.Equal(37, module.Read(reference, holders[1], frame)!.Value);
            Assert.True(f.Room.GetRoomItemHandler().RemoveTemporaryFloorItem(copies[1]));
            Assert.Empty(module.GetStoredHolders(variableId));
            operandId = ModernWiredDatabaseProbe.Insert(admin, "items", new() { ["user_id"] = userId, ["room_id"] = roomId, ["base_item"] = baseId, ["extra_data"] = "", ["wall_pos"] = "" });
            var operand = WiredNativeEditorProjection.DefaultNative(Descriptor("wf_var_context")) with { OwnedIntParams = [1], Text = "operand" };
            admin.Execute("INSERT INTO wired_item_configurations(item_id,box_name,schema_version,configuration) VALUES (@operandId,'wf_var_context',2,@config)", new { operandId, config = System.Text.Json.JsonSerializer.Serialize(operand) });
            var operandFrame = new WiredVariableFrame(roomId, []);
            Assert.True(module.Mutate(new(WiredVariableTarget.Context, $"custom:{operandId}"), new(WiredVariableTarget.Context, 0, 0), WiredVariableMutation.Give, 42, operandFrame));
            config = config with { TemporaryPlacement = config.TemporaryPlacement! with { ValueIsVariable = true, ValueTarget = 2 }, VariableIds = [$"furni:{variableId}", $"ctx:{operandId}"] };
            Assert.True(WiredNativeTestSupport.TryValidateRuntime(action, config, out config, out _));
            action.ApplyConfiguration(config);
            context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
            context.VariableFrame = operandFrame;
            Assert.True(action.Execute(context));
            copies = f.Room.GetRoomItemHandler().GetFloor.Where(item => item.IsTemporary).ToArray();
            holders = copies.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray();
            frame = new(roomId, holders);
            Assert.All(holders, holder => Assert.Equal(42, module.Read(reference, holder, frame)!.Value));
            Assert.Equal(0, admin.ExecuteScalar<int>("SELECT COUNT(*) FROM wired_variable_values WHERE definition_id=@variableId", new { variableId }));
            f.Room.GetRoomItemHandler().Dispose();
            Assert.Empty(module.GetStoredHolders(variableId));

        }
        finally {
            f.Engine.Clear();
            admin.Execute("DELETE FROM wired_variable_values WHERE definition_id=@variableId", new { variableId });
            admin.Execute("DELETE FROM wired_item_configurations WHERE item_id IN (@variableId,@operandId)", new { variableId, operandId });
            admin.Execute("DELETE FROM items WHERE user_id=@userId", new { userId });
            admin.Execute("DELETE FROM rooms WHERE id=@roomId", new { roomId });
            admin.Execute("DELETE FROM users WHERE id=@userId", new { userId });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(127)]
    public void NativeProjectileMaskControlsOnlyRequestedFlightOutputs(int mask)
    {
        using var f = new TeleportFixture();
        f.Target.Definition.Stackable = true;
        var item = MakeItem(90, "wf_xtra_rotate_to_dir");
        typeof(Item).GetField("_room", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(item, f.Room);
        f.Items[item.Id] = item;
        var addon = Assert.IsType<WiredAddonBox>(f.Room.GetWired().CreateConfiguredBox(item));
        var native = WiredNativeEditorProjection.DefaultNative(addon.Descriptor) with
        {
            OwnedIntParams = WiredNativeEditorProjection.DefaultNative(addon.Descriptor).OwnedIntParams.SetItem(11, mask),
            PrimaryItems = [new(f.Target.Id, false)]
        };
        Assert.True(WiredNativeEditorProjection.TryCompile(item.Id, addon.Descriptor, native, out var compiled));
        Assert.True(WiredConfigurationSave.TrySave(addon, compiled, TestWiredConfigurationStore.Instance, out var error), error);
        var context = Context(f.Room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
        context.NowMilliseconds = 1000;
        Assert.True(addon.Apply(context));
        Assert.True(new WiredRoomMovement(f.Room.GetWired().DispatchWalkTransition).MoveFurniture(context, f.Target, 2, 1, 0, null));
        string[] keys = ["tiles_traveled", "user_collisions", "furni_collisions", "position.x", "position.y", "position.altitude", "is_traveling"];
        var flights = WiredProjectileFlights.For(f.Room);

        for (var bit = 0; bit < keys.Length; bit++) {
            var value = flights.Read(f.Target, "@projectile.animation." + keys[bit], 1000);
            Assert.Equal((mask & (1 << bit)) != 0, value.HasValue);
        }

        // A later native configuration with no requested outputs must replace an earlier enabled flight.
        context.Policy.Addons.Projectile = context.Policy.Addons.Projectile! with { VariableMask = 0 };
        Assert.True(new WiredRoomMovement(f.Room.GetWired().DispatchWalkTransition).MoveFurniture(context, f.Target, 1, 1, 0, null));
        Assert.All(keys, key => Assert.Null(flights.Read(f.Target, "@projectile.animation." + key, 1000)));
    }

    [Fact]
    public void ActualProjectileMoveSamplesPacketClockAndLaunchCollisionsWithoutLateArrivals()
    {
        using var f = new TeleportFixture();
        var room = f.Room;
        var map = room.GetGameMap();
        var definition = MakeItem(5, "projectile").Definition;
        definition.Stackable = true;
        definition.Walkable = true;
        var mover = Assert.IsType<Item>(room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 1, 0, 1, 0, 0.25));
        f.Target.Definition.Stackable = true;
        map.AddToMap(f.Target);
        var obstacle = MakeItem(2, "obstacle");
        obstacle.Definition.Stackable = true;
        obstacle.SetState(2, 1, 0, Gamemap.GetAffectedTiles(1, 1, 2, 1, 0));
        f.Items[2] = obstacle;
        map.AddToMap(obstacle);
        var user = Bot(room, 8);
        user.SetPos(1, 1, 0);
        RoomUsers(room)[8] = user;
        map.AddUserToMap(user, new(1, 1));
        var launchUser = Bot(room, 9);
        launchUser.SetPos(0, 1, 0);
        RoomUsers(room)[9] = launchUser;
        map.AddUserToMap(launchUser, new(0, 1));
        var flights = WiredProjectileFlights.For(room);
        Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 1000));
        var context = Context(room, new(WiredEventKind.Use), f.Items.Values.ToArray(), RoomUsers(room).Values.ToArray());
        context.NowMilliseconds = 1000;
        context.Policy.Addons.Projectile = new(new HashSet<uint> { mover.Id }, null, 0, null, Plus.HabboHotel.Items.Wired.Modern.Addons.WiredProjectileDistance.Normal, 0) { VariableMask = 127 };
        context.Policy.Addons.AnimationTimeMs = 800;
        var movements = new WiredRoomMovement(room.GetWired().DispatchWalkTransition);
        Assert.True(movements.MoveFurniture(context, mover, 2, 1, 0, 2.25));
        Assert.Equal(0, flights.Read(mover, "@projectile.animation.position.x", 999));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.is_traveling", 1000));
        Assert.Equal(0, flights.Read(mover, "@projectile.animation.user_collisions", 1000)); // Launch tile is excluded.
        Assert.True(WiredRoomOperations.RelocateAvatar(room, user, 1, 2, false));
        var late = Bot(room, 10);
        late.SetPos(2, 1, 0);
        RoomUsers(room)[10] = late;
        map.AddUserToMap(late, new(2, 1));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.tiles_traveled", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.x", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.y", 1400));
        Assert.Equal(125, flights.Read(mover, "@projectile.animation.position.altitude", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.furni_collisions", 1400));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.user_collisions", 1400));
        Assert.Null(flights.Read(mover, "@projectile.animation.is_traveling", 1800));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.tiles_traveled", 9999));
        Assert.Equal(225, flights.Read(mover, "@projectile.animation.position.altitude", 9999));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.furni_collisions", 9999));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.user_collisions", 9999));
        Assert.False(movements.MoveFurniture(context, mover, 5, 1, 0, null));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 9999));
    }

    [Fact]
    public void ProjectileScopeRepeatedFlightInstantMoveAndReusedIdentityHaveRealLifecycle()
    {
        using var f = new TeleportFixture();
        var room = f.Room;
        var definition = MakeItem(5, "projectile").Definition;
        definition.Stackable = true;
        var mover = Assert.IsType<Item>(room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 1, 0, 1, 0));
        var flights = WiredProjectileFlights.For(room);
        var movement = new WiredRoomMovement(room.GetWired().DispatchWalkTransition);
        var ctx = Context(room, new(WiredEventKind.Use), f.Items.Values.ToArray(), [f.User]);
        ctx.NowMilliseconds = 1000;
        ctx.Policy.Addons.DisableAnimation = true;
        ctx.Policy.Addons.Projectile = new(new HashSet<uint> { 999 }, null, 0, null, Plus.HabboHotel.Items.Wired.Modern.Addons.WiredProjectileDistance.Normal, 0) { VariableMask = 127 };
        Assert.True(movement.MoveFurniture(ctx, mover, 1, 2, 0, null));
        Assert.Null(flights.Read(mover, "@projectile.animation.tiles_traveled", 1000));
        ctx.Policy.Addons.Projectile = ctx.Policy.Addons.Projectile with { ItemIds = new HashSet<uint> { mover.Id } };
        Assert.True(movement.MoveFurniture(ctx, mover, 2, 2, 0, null));
        Assert.Null(flights.Read(mover, "@projectile.animation.is_traveling", 1000));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 1000));
        ctx.NowMilliseconds = 2000;
        ctx.Policy.Addons.DisableAnimation = false;
        ctx.Policy.Addons.AnimationTimeMs = 1000;
        Assert.True(movement.MoveFurniture(ctx, mover, 0, 2, 0, null));
        Assert.Equal(2, flights.Read(mover, "@projectile.animation.position.x", 2000));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.position.x", 2500));
        Assert.Equal(1, flights.Read(mover, "@projectile.animation.is_traveling", 2500));
        var sameIdDifferentItem = MakeItem(mover.Id, "replacement");
        f.Items[mover.Id] = sameIdDifferentItem;
        Assert.Null(flights.Read(sameIdDifferentItem, "@projectile.animation.position.x", 2500));
        Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 2500));
        f.Items[mover.Id] = mover;
        Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 3000));
        flights.Forget(sameIdDifferentItem);
        Assert.NotNull(flights.Read(mover, "@projectile.animation.position.x", 3000));
        flights.Forget(mover);
        Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 3000));
        Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 4000));
        flights.Clear();
        Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 4000));
        Assert.True(flights.Begin(mover, 2, 2, 0, 1000, 5000));
        Assert.True(room.GetRoomItemHandler().RemoveTemporaryFloorItem(mover));
        Assert.Null(flights.Read(mover, "@projectile.animation.position.x", 5000));
    }

    public class RecordingProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> InvokeMethod = (_, _) => null;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args);
    }
    private static WiredBoxDescriptor Descriptor(string name)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));

        return descriptor;
    }
    private static Item MakeItem(uint id, string name) => new()
    {
        Id = id,
        ExtraData = new LegacyDataFormat { Data = "0" },
        Definition = new()
        {
            Type = ItemType.Floor,
            ItemName = name,
            InteractionName = name,
            Width = 1,
            Length = 1,
            Modes = 2,
            AdjustableHeights = [],
            VendingIds = [],
            PublicName = name
        }
    };
    private static (Room Room, Gamemap Map, ConcurrentDictionary<uint, Item> Items) World(IRoomItemStore? store = null, string heightmap = "000\r000\r000")
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        typeof(Room).GetField("_interactionClock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, TimeProvider.System);
        var map = new Gamemap(room, new RoomModel("wired-test", 0, 0, 0, 0, heightmap, 0, 0, true), TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        var handler = new RoomItemHandling(room, store ?? TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        TestRoomUserSnapshots.Install(room);
        typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[map.Model.MapSizeX, map.Model.MapSizeY]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[map.Model.MapSizeX, map.Model.MapSizeY]);

        return (room, map, (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!);
    }
    private static WiredRuntimeContext Context(Room room, WiredRuntimeEvent @event, Item[] items, RoomUser[] users) =>
        new(room, @event, new(() => items, () => users), new UnusedOperations());
    private sealed class RecordingPlacementStore : IRoomItemStore
    {
        public List<(uint Id, uint Room, int X, int Y, double Z, int Rotation)> FloorPlacements { get; } = [];
        public void AssignOwner(uint itemId, int userId) => throw new NotSupportedException();
        public void ClearRoom(uint itemId) => throw new NotSupportedException();
        public void SaveWallPosition(uint itemId, string wallPosition) => throw new NotSupportedException();
        public void SaveMoved(IReadOnlyList<RoomItemSave> items) => throw new NotSupportedException();
        public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) => FloorPlacements.Add((itemId, roomId, x, y, z, rotation));
        public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) => throw new NotSupportedException();
    }
    private sealed class PlaceCatalogDirectory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id switch
        {
            10 => new(10, 1, 5, "spawn", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true),
            11 => new(11, 1, 5, "operand", WiredVariableTarget.Context, WiredVariableAvailability.RoomActive, true),
            _ => null
        };
        public uint? GetRoomOwner(uint roomId) => 5;
    }
    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
    private sealed class ResetOperations : IWiredRuntimeOperations
    {
        public int Resets { get; private set; }
        public List<Item> Targets { get; } = [];
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets)
        {
            Resets++;
            Targets.AddRange(targets);
        }
    }
    private sealed class CountingClock(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
    {
        public int Calls { get; private set; }
        public override TimeZoneInfo LocalTimeZone => zone;
        public override DateTimeOffset GetUtcNow()
        {
            Calls++;

            return now;
        }
    }
}
