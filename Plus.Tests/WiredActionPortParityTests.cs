using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Conditions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Instance;
using Plus.HabboHotel.Rooms.Games.Teams;
using Xunit;
using Xunit.Abstractions;

namespace Plus.Tests;

public sealed class WiredActionPortParityTests(ITestOutputHelper output)
{
    [Fact]
    public void NearestPlayerUsesManhattanRangeAndOrdering()
    {
        var (room, _, _) = World();
        var item = Floor(1, 0, 0);
        var diagonal = Avatar(room, 1, 2, 2);
        var cardinal = Avatar(room, 2, 3, 0);
        Assert.Null(WiredDirectionalActions.Nearest(item, [diagonal]));
        Assert.Same(cardinal, WiredDirectionalActions.Nearest(item, [diagonal, cardinal]));
    }

    [Fact]
    public void ChaseStepsTowardADiagonalPlayerInsteadOfPublishingCollision()
    {
        var (room, map, items) = World();
        var mover = Floor(1, 1, 1);
        Place(map, items, mover);
        var user = Avatar(room, 1, 2, 2);
        var events = new List<WiredRuntimeEvent>();
        var action = Box(room, "wf_act_chase", [100], [mover.Id], events.Add);
        Assert.True(action.Execute(Context(room, [mover], [user])));
        Assert.Equal((2, 1), (mover.GetX, mover.GetY));
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("wf_act_chase", true)]
    [InlineData("wf_act_chase", false)]
    [InlineData("wf_act_flee", true)]
    [InlineData("wf_act_flee", false)]
    [InlineData("wf_act_move_to_dir", true)]
    [InlineData("wf_act_move_to_dir", false)]
    public void DirectionalActionsAttemptEveryStackedItemIndependently(string name, bool topFirst)
    {
        var (room, map, items) = World();
        var bottom = Floor(2, 2, 2);
        bottom.Definition.Stackable = true;
        bottom.Definition.Height = 0.5;
        var top = Floor(1, 2, 2);
        top.SetState(2, 2, 0.5, Gamemap.GetAffectedTiles(1, 1, 2, 2, 0));
        Place(map, items, bottom);
        Place(map, items, top);
        var user = Avatar(room, 1, name == "wf_act_flee" ? 0 : 4, 2);
        var action = Box(room, name, name == "wf_act_move_to_dir" ? [2, 0, 100, 0] : [100], [1, 2]);
        Assert.True(action.Execute(Context(room, topFirst ? [top, bottom] : [bottom, top], [user])));
        Assert.Equal((topFirst ? 2 : 3, 2, 0.0), (bottom.GetX, bottom.GetY, bottom.GetZ));
        Assert.Equal((3, 2, topFirst ? 0.0 : 0.5), (top.GetX, top.GetY, top.GetZ));
    }

    [Theory]
    [InlineData(0, 3, 3, true)]
    [InlineData(0, 2, 0, true)]
    [InlineData(0, 0, 1, false)]
    [InlineData(1, 0, 1, true)]
    [InlineData(2, 2, 0, false)]
    public void SlideKeepsWalkingWhenDiagonalStepDistanceDecreases(int mode, int x, int y, bool resumes)
    {
        var (room, map, _) = World();
        var user = Avatar(room, 1, 0, 0);
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(room.GetRoomUserManager())!;
        users[user.VirtualId] = user;
        map.AddUserToMap(user, new(0, 0));
        user.IsWalking = true;
        user.GoalX = 5;
        user.GoalY = 0;
        Assert.True(new WiredRoomMovement((_, _, _) => { }).MoveAvatar(Context(room, [], [user]), user, x, y, false, mode));
        Assert.Equal(resumes, user.PathRecalcNeeded);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void SnapshotQuantifierIncludesMissingPickedFurniture(int quantifier, bool matches)
    {
        var first = Floor(1, 1, 1);
        var config = new WiredConfiguration
        {
            IntParams = [1, 0, 0, 0, 100, quantifier],
            SelectedItems = [1, 2],
            Snapshots = [new(1, 0, 1, 1, 0, 0, "0"), new(2, 0, 2, 2, 0, 0, "0")]
        };
        Assert.Equal(matches, WiredItemConditions.MatchesSnapshot(config, [first]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void GroupTargetEditorTranslatesTheLayoutOntoItsChosenFurnitureOrUser(int targetIsUser)
    {
        var (room, map, items) = World();
        var first = Floor(1, 0, 1);
        var second = Floor(2, 1, 1);
        var target = Floor(3, 3, 3);
        target.Definition.Stackable = true;
        Place(map, items, first);
        Place(map, items, second);
        Place(map, items, target);
        var user = Avatar(room, 1, 3, 3);
        var action = Box(room, "wf_act_move_furni_as_group", [targetIsUser, 1, -1, 100, 100, 0], [1, 2], text: "3");
        Assert.True(action.Execute(Context(room, [first, second, target], [user])));
        Assert.Equal((4, 2), (first.GetX, first.GetY));
        Assert.Equal((5, 2), (second.GetX, second.GetY));
        Assert.Equal((3, 3), (target.GetX, target.GetY));
    }

    [Fact]
    public void GroupRejectsTheWholeTranslationWhenOneMemberWouldLeaveTheRoom()
    {
        var (room, map, items) = World();
        var first = Floor(1, 0, 1);
        var second = Floor(2, 1, 1);
        var target = Floor(3, 5, 3);
        Place(map, items, first);
        Place(map, items, second);
        Place(map, items, target);
        var action = Box(room, "wf_act_move_furni_as_group", [0, 0, 0, 100, 100, 0], [1, 2], text: "3");
        Assert.False(action.Execute(Context(room, [first, second, target], [])));
        Assert.Equal((0, 1), (first.GetX, first.GetY));
        Assert.Equal((1, 1), (second.GetX, second.GetY));
    }

    [Fact]
    public void MoveToEditorResolvesMoversSeparatelyFromItsPickedTarget()
    {
        var (room, map, items) = World();
        var mover = Floor(1, 0, 1);
        var target = Floor(3, 3, 3);
        Place(map, items, mover);
        Place(map, items, target);
        var action = Box(room, "wf_act_move_furni_to", [2, 2, 100, 100], [1], text: "3");
        Assert.True(action.Execute(Context(room, [mover, target], [])));
        Assert.Equal((5, 3), (mover.GetX, mover.GetY));
        Assert.Equal((3, 3), (target.GetX, target.GetY));
    }

    [Theory]
    [InlineData(true, false, null, 1)]
    [InlineData(false, false, null, 1)]
    [InlineData(true, true, 1, 1)]
    [InlineData(false, true, 1, 1)]
    [InlineData(true, true, null, 2)]
    [InlineData(false, true, null, 2)]
    [InlineData(true, true, 0, 0)]
    [InlineData(false, true, -1, 0)]
    public void VariableSortAndQuantityAddonsFilterTheActualSelectorPoolInStackOrder(bool sortFirst, bool variableQuantity, int? variableCount, int expectedCount)
    {
        var (room, _, items) = World();
        room.Id = 1;
        room.OwnerId = 5;
        var movers = Enumerable.Range(1, 3).Select(id => Floor((uint)id, id, 1)).ToArray();

        foreach (var item in movers) {
            item.OwnerId = 5;
            items[item.Id] = item;
        }

        var holders = movers.Select(WiredVariableRuntimeFrames.FurniHolder).ToArray();
        var module = new WiredVariableModule(1, new FurnitureDirectory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var frame = new WiredVariableFrame(1, holders);

        for (var i = 0; i < holders.Length; i++) {
            module.Mutate(new(WiredVariableTarget.Furni, "custom:10"), holders[i], WiredVariableMutation.Give, new[] { 10, 30, 20 }[i], frame);
        }

        if (variableQuantity && variableCount is { } amount) {
            // First selected holder lacks the operand; the next readable holder supplies it.
            Assert.True(module.Mutate(new(WiredVariableTarget.Furni, "custom:11"), holders[1], WiredVariableMutation.Give, amount, frame));
            Assert.True(module.Mutate(new(WiredVariableTarget.Furni, "custom:11"), holders[2], WiredVariableMutation.Give, 2, frame));
        }

        var world = new WiredSelectorWorld(6, 6, movers.Select(item => new WiredSelectorFurniture(item.Id, 0, "test", "0", item.GetX, item.GetY, 0, 0, [(item.GetX, item.GetY)])).ToArray(), []);
        var errors = new List<Exception>();
        var engine = new WiredStackEngine(() => 0, _ => true, _ => true, _ => { }, errors.Add);
        engine.BindRuntime(room, new(() => items.Values, () => []), new UnusedOperations());
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_game_starts", out var triggerDescriptor));
        var trigger = new WiredModernTrigger(room, Floor(50, 5, 5), triggerDescriptor);
        trigger.ApplyConfiguration(WiredTriggerConfiguration.Defaults("wf_trg_game_starts"));
        engine.Add(trigger);
        Assert.True(WiredBoxRegistry.TryGet("wf_slc_furni_picks", out var selectorDescriptor));
        var selector = new WiredSelectorBox(room, Floor(51, 5, 5), selectorDescriptor, new(), TestGroupManager.Empty, readWorld: _ => world);
        selector.ApplyConfiguration(new() { SelectedItems = [1, 2, 3] });
        engine.Add(selector);
        Assert.True(WiredBoxRegistry.TryGet("wf_xtra_filter_furni_by_var", out var sortDescriptor));
        var sort = new WiredVariableAddonBox(room, Floor(sortFirst ? 52u : 53u, 5, 5), sortDescriptor, module, _ => new Dictionary<int, string>());
        sort.ApplyConfiguration(new() { IntParams = [0, 0, 2, 1, 0, 0], Text = "custom:10\t" });
        engine.Add(sort);
        Assert.True(WiredBoxRegistry.TryGet("wf_xtra_filter_furni", out var quantityDescriptor));
        var quantity = new WiredAddonBox(room, Floor(sortFirst ? 53u : 52u, 5, 5), quantityDescriptor, new(), TestGroupManager.Empty,
            variables: context => WiredSelectorVariableBridge.Create(context, module), readWorld: _ => world);
        quantity.ApplyConfiguration(variableQuantity ? new() { IntParams = [2, 1, 1], Text = "custom:11" } : new() { IntParams = [1] });
        engine.Add(quantity);
        Assert.True(WiredBoxRegistry.TryGet("wf_act_change_var_val", out var actionDescriptor));
        var action = new WiredVariableConfiguredBox(room, Floor(54, 5, 5), actionDescriptor, new(module, TimeProvider.System));
        action.ApplyConfiguration(new() { IntParams = [1, 0, 0, 7, 1, 0, 200, 0, 0], Text = "custom:10" });
        engine.Add(action);
        Assert.True(engine.DispatchSynchronously(new(WiredEventKind.GameStart)));
        engine.OnFastCycle();
        Assert.Empty(errors);
        var expected = expectedCount == 0 ? new long[] { 10, 30, 20 } : expectedCount == 2
            ? sortFirst ? new long[] { 10, 7, 7 } : [7, 7, 20]
            : sortFirst ? new long[] { 10, 7, 20 } : [7, 30, 20];
        Assert.Equal(expected, holders.Select(holder => module.Read(new(holder.Target, "custom:10"), holder, frame)!.Value));
    }

    [Fact]
    public void AltitudeAdditionCanPassFortyWithinThePlatformTopLimit()
    {
        var (room, map, items) = World();
        var item = Floor(1, 1, 1);
        item.SetState(1, 1, 30, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0));
        Place(map, items, item);
        var action = Box(room, "wf_act_set_altitude", [0, 100], [1], text: "20");
        Assert.True(action.Execute(Context(room, [item], [])));
        Assert.Equal(50, item.GetZ);
    }

    [Theory]
    [InlineData(1, "50", 50)]
    [InlineData(1, "80", 79)]
    [InlineData(0.65, "80", 79.35)]
    public void AltitudeSetHonorsTheEightyTileTopLimitAndDefinitionHeight(double height, string requested, double expected)
    {
        var (room, map, items) = World();
        var item = Floor(1, 1, 1);
        item.Definition.Height = height;
        Place(map, items, item);
        var action = Box(room, "wf_act_set_altitude", [2, 100], [1], text: requested);
        Assert.True(action.Execute(Context(room, [item], [])));
        Assert.Equal(expected, item.GetZ);
    }

    [Theory]
    [InlineData("wf_act_give_score")]
    [InlineData("wf_act_give_score_tm")]
    public void ScoreQuotaEditorCountsPerPlayerAndResetsPerGame(string name)
    {
        var (room, _, _) = World();
        var user = Avatar(room, 1, 1, 1);
        user.Team = Team.Red;
        room.GetGameManager().AddPointToTeam(Team.Red, 20);
        var action = Box(room, name, name == "wf_act_give_score" ? [5, 1, 0, 2] : [5, 1, 1, 0, 2], []);
        var context = Context(room, [], [user]);
        Assert.True(action.Execute(context));
        Assert.True(action.Execute(context));
        Assert.False(action.Execute(context));
        Assert.Equal(10, room.GetGameManager().Points[1]);
        WiredGameState.For(room).ResetQuotas();
        Assert.True(action.Execute(context));
        Assert.Equal(5, room.GetGameManager().Points[1]);
    }

    [Theory]
    [InlineData("custom:10", "internal:~area_hide.width", true)]
    [InlineData("custom:10", "internal:@position.x", true)]
    [InlineData("custom:10", "custom:11", true)]
    [InlineData("internal:~area_hide.inverted", "custom:11", false)]
    public void SnapshotPlacementVariableRolesAcceptNumericSmartSourcesAndOnlyCustomDestinations(string destination, string source, bool expected)
    {
        var proposed = new WiredConfiguration
        {
            IntParams = [1, 0, 1, 2, 0, 0, 0, 100, 0, 1, 1, 0, 1, 100, 0],
            Text = "1\t" + destination + "\t" + source,
            SelectedItems = [2]
        };
        Assert.Equal(expected, WiredTemporaryFurnitureActions.TryDecodeEditor(proposed, out var decoded));

        if (expected) {
            Assert.Equal(new[] { destination, source }, decoded.VariableIds.ToArray());
            Assert.Equal(new uint[] { 1 }, decoded.SecondarySelectedItems.ToArray());
            Assert.Equal(new uint[] { 2 }, decoded.SelectedItems.ToArray());
        }
    }


    [Theory]
    [InlineData(false, 0, 0, 23, 23, 1)]
    [InlineData(true, 0, 0, 26, 26, 2)]
    [InlineData(false, 0, 1, 13, 26, 2)]
    [InlineData(false, 1, 0, 20, 23, 2)]
    [InlineData(false, 1, 1, 10, 23, 1)]
    public void RealScalarActionsBatchWithinAStackUnlessOrderedOrSeparatedByADelay(bool ordered, int addDelay, int multiplyDelay, int firstValue, int expected, int notifications)
    {
        var (room, _, items) = World();
        room.Id = 1;
        room.OwnerId = 5;
        var target = Floor(1, 0, 1);
        target.OwnerId = 5;
        items[target.Id] = target;
        var holder = WiredVariableRuntimeFrames.FurniHolder(target);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "custom:10");
        var module = new WiredVariableModule(1, new FurnitureDirectory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        var frame = new WiredVariableFrame(1, [holder]);
        module.Mutate(reference, holder, WiredVariableMutation.Give, 10, frame);
        module.DrainChanges();
        long now = 0;
        var errors = new List<Exception>();
        var engine = new WiredStackEngine(() => now, _ => true, _ => true, _ => { }, errors.Add);
        engine.BindRuntime(room, new(() => items.Values, () => []), new UnusedOperations());
        var triggerItem = Floor(50, 5, 5);
        Assert.True(WiredBoxRegistry.TryGet("wf_trg_game_starts", out var triggerDescriptor));
        var trigger = new WiredModernTrigger(room, triggerItem, triggerDescriptor);
        trigger.ApplyConfiguration(WiredTriggerConfiguration.Defaults("wf_trg_game_starts"));
        engine.Add(trigger);
        Add(51, 1, 3, addDelay);
        Add(52, 3, 2, multiplyDelay);

        if (ordered) {
            Assert.True(WiredBoxRegistry.TryGet("wf_xtra_exec_in_order", out var descriptor));
            var addon = new WiredAddonBox(room, Floor(53, 5, 5), descriptor, new(), TestGroupManager.Empty,
                readWorld: _ => new WiredSelectorWorld(6, 6, [], []));
            addon.ApplyConfiguration(new());
            engine.Add(addon);
        }

        var accepted = engine.DispatchSynchronously(new(WiredEventKind.GameStart));
        Assert.Empty(errors);
        Assert.True(accepted);
        Assert.Equal(10, module.Read(reference, holder, frame)!.Value);
        engine.OnFastCycle();
        Assert.Equal(firstValue, module.Read(reference, holder, frame)!.Value);
        now = 500;
        engine.OnFastCycle();
        now = 1000;
        engine.OnFastCycle();
        Assert.Equal(expected, module.Read(reference, holder, frame)!.Value);
        Assert.Equal(notifications, module.DrainChanges().Count);
        Assert.Empty(errors);

        void Add(uint id, int operation, int operand, int actionDelay)
        {
            var item = Floor(id, 5, 5);
            Assert.True(WiredBoxRegistry.TryGet("wf_act_change_var_val", out var descriptor));
            var box = new WiredVariableConfiguredBox(room, item, descriptor, new(module, TimeProvider.System));
            Assert.True(box.TryValidateConfiguration(new()
            {
                IntParams = [1, operation, 0, operand, 1, 0, 100, 0, 100],
                Text = "custom:10",
                SelectedItems = [1],
                Delay = actionDelay
            }, out var config, out var error), error);
            box.ApplyConfiguration(config);
            engine.Add(box);
        }
    }

    [Theory]
    [InlineData("bb_counter", InteractionType.Banzaicounter)]
    [InlineData("fball_counter", InteractionType.Counter)]
    [InlineData("es_counter", InteractionType.Freezetimer)]
    public void NativeTimerClicksAndWiredControlsDriveActualRoomGames(string name, InteractionType type)
    {
        var (room, map, items) = World();
        var wired = new WiredComponent(room, TestLogging.Logger, TimeProvider.System, TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance,
            TestWiredConfigurationStore.Instance, TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance,
            TestWiredClients.Empty, TestGroupManager.Empty, TestWiredDefinitions.Unused, TestWiredCommands.Unused, TestWiredAccess.Unused, TestItemRuntime.Travel);
        typeof(Room).GetField("_wiredComponent", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, wired);
        var timer = Floor(1, 0, 1);
        timer.Definition.ItemName = name;
        timer.Definition.InteractionName = "game_timer";
        timer.Definition.InteractionType = type;
        timer.LegacyDataString = "8";
        timer.Attach(room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        Place(map, items, timer);
        wired.AttachRoomItem(timer);
        var clocks = (WiredCounterController)typeof(WiredComponent).GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        var interactor = TestItemRuntime.Interactors.Create(timer, TimeProvider.System);
        interactor.OnTrigger(null, timer, 0, false);
        Assert.False(Active());
        interactor.OnTrigger(null, timer, 0, true);
        Assert.True(Active());
        Assert.True(clocks.IsRunning(timer));
        var pause = Box(room, "wf_act_control_clock", [3, 100], [1], clocks: clocks);
        Assert.True(pause.Execute(Context(room, [timer], [])));
        Assert.False(Active());
        Assert.Equal(8000, clocks.ReadMilliseconds(timer));
        interactor.OnTrigger(null, timer, 0, true);
        Assert.True(Active());
        interactor.OnTrigger(null, timer, 2, true);
        Assert.False(Active());
        Assert.Equal(30000, clocks.ReadMilliseconds(timer));

        bool Active() => type switch
        {
            InteractionType.Banzaicounter => room.GetBanzai().IsBanzaiActive,
            InteractionType.Freezetimer => room.GetFreeze().GameIsStarted,
            _ => room.GetSoccer().GameIsStarted
        };
    }


    private static WiredModernAction Box(Room room, string name, int[] parameters, uint[] selected, Action<WiredRuntimeEvent>? publish = null, string text = "", WiredCounterController? clocks = null)
    {
        var action = CreateBox(room, name, publish, clocks: clocks);
        var proposed = new WiredConfiguration { IntParams = [.. parameters], SelectedItems = [.. selected], Text = text };

        if (name is "wf_act_move_to_dir" or "wf_act_set_altitude" or "wf_act_move_furni_as_group"
            or "wf_act_control_clock" or "wf_act_give_score") {
            ModernWiredRuntimeTests.LoadStoredRuntime(action, name, proposed);
        }
        else {
            Assert.True(action.TryValidateConfiguration(proposed, out var config, out var error), error);
            action.ApplyConfiguration(config);
        }

        return action;
    }

    private static WiredModernAction CreateBox(Room room, string name, Action<WiredRuntimeEvent>? publish = null,
        WiredCounterController? clocks = null, IItemDataManager? definitions = null)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));

        return new(room, Floor(50, 5, 5), descriptor, clocks ?? new(), publish ?? (_ => { }),
            (_, _, _) => { }, new(), TestLogging.Logger, TimeProvider.System, TestWiredRewardService.Instance,
            TestBotManagementStore.Instance, TestWiredClients.Empty, definitions ?? TestWiredDefinitions.Unused, TestItemRuntime.Travel);
    }

    private sealed class ItemDefinitions(ItemDefinition definition) : IItemDataManager
    {
        public void Init() { }
        public ItemDefinition? GetItemByName(string name) => Items.Values.FirstOrDefault(item => item.ItemName == name);
        public Dictionary<int, uint> Gifts { get; } = [];
        public Dictionary<uint, ItemDefinition> Items { get; } = new() { [definition.Id] = definition };
    }

    private static RoomUser Avatar(Room room, int id, int x, int y)
    {
        var user = new RoomUser(id, 0, id, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        user.SetPos(x, y, 0);

        return user;
    }

    private static Item Floor(uint id, int x, int y)
    {
        var item = new Item
        {
            Id = id,
            ExtraData = new LegacyDataFormat { Data = "0" },
            Definition = new()
            {
                Type = ItemType.Floor,
                ItemName = "test",
                InteractionName = "test",
                Width = 1,
                Length = 1,
                Modes = 2,
                AdjustableHeights = [],
                VendingIds = [],
                PublicName = "test"
            }
        };
        item.SetState(x, y, 0, Gamemap.GetAffectedTiles(1, 1, x, y, 0));

        return item;
    }

    private static void Place(Gamemap map, ConcurrentDictionary<uint, Item> items, Item item)
    {
        items[item.Id] = item;
        map.AddToMap(item);
    }

    private static (Room Room, Gamemap Map, ConcurrentDictionary<uint, Item> Items) World()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        typeof(Room).GetField("_interactionClock", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, TimeProvider.System);
        var map = new Gamemap(room, new RoomModel("wired-parity", 0, 0, 0, 0, string.Join('\r', Enumerable.Repeat("000000", 6)), 0, 0, true),
            TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance,
            TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room,
            new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(),
                TestChatEmotions.Unused, TestBotAiFactory.Inert, TestGameClientManager.Empty, TestItemRuntime.Travel));
        TestRoomUserSnapshots.Install(room);
        typeof(Gamemap).GetProperty("GameMap")!.SetValue(map, new byte[6, 6]);
        typeof(Gamemap).GetProperty("EffectMap")!.SetValue(map, new byte[6, 6]);

        return (room, map, (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!);
    }

    private static WiredRuntimeContext Context(Room room, Item[] items, RoomUser[] users)
    {
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Use), new(() => items, () => users), new UnusedOperations());

        if (users.FirstOrDefault() is { } actor) {
            context.Triggering.UserIds.Add(actor.VirtualId);
        }

        context.Policy.Addons.DisableAnimation = true;

        return context;
    }

    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }

    private sealed class FurnitureDirectory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is 10 or 11 ? new(id, 1, 5, "value", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
}
