using System.Drawing;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
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

public class ModernWiredBehaviorTests
{
    [Fact]
    public void RelativeMovementDecodesTheExistingFiveEditorFields()
    {
        var item = Item(1, 5, 7);
        var requested = new List<(uint, int, int)>();
        var changed = Execute("wf_act_rel_mov", new() { IntParams = [0, 2, 1, 3, 100] }, [item], [], [],
            (furni, x, y, _, _) => { requested.Add((furni.Id, x, y)); return true; });
        Assert.True(changed);
        Assert.Equal(new[] { (1u, 3, 10) }, requested);
    }

    [Fact]
    public void BlockedMovementDoesNotReportSuccess()
    {
        Assert.False(Execute("wf_act_rel_mov", new() { IntParams = [1, 1, 1, 0, 100] },
            [Item(1, 0, 0)], [], [], (_, _, _, _, _) => false));
    }

    [Fact]
    public void ConfigurationDecodesBothRolesAndPreservesTheEditorPayload()
    {
        var editor = new WiredConfiguration { IntParams = [100, 201], Text = "2;3;2", SelectedItems = [1, 1] };
        Assert.True(WiredMovementConfiguration.TryValidate("wf_act_furni_to_furni", editor, out var runtime, out _));
        Assert.Equal(editor.IntParams, runtime.IntParams);
        Assert.Equal("2;3;2", runtime.Text);
        Assert.Equal(100, runtime.FurniSources["movers"]);
        Assert.Equal(201, runtime.FurniSources["targets"]);
        Assert.Equal(new uint[] { 1 }, runtime.SelectedItems);
        Assert.Equal(new uint[] { 2, 3 }, runtime.SecondarySelectedItems);
    }

    [Fact]
    public void InvalidConfigurationCannotRunAnActionOrChangeTheProposedPayload()
    {
        var proposed = new WiredConfiguration { IntParams = [1, int.MaxValue, 1, 0, 100] };
        Assert.False(WiredMovementConfiguration.TryValidate("wf_act_rel_mov", proposed, out var result, out _));
        Assert.Same(proposed, result);
        var called = false;
        Assert.False(Execute("wf_act_rel_mov", proposed, [Item(1, 0, 0)], [], [],
            (_, _, _, _, _) => { called = true; return true; }));
        Assert.False(called);
    }

    [Fact]
    public void GroupMovesItsLeadingEdgeFirstAndContinuesPastBlockedMembers()
    {
        var sequence = new List<uint>();
        var changed = Execute("wf_act_move_furni_as_group", new() { IntParams = [2, 100] },
            [Item(1, 0, 0), Item(2, 1, 0), Item(3, 2, 0)], [], [],
            (item, _, _, _, _) => { sequence.Add(item.Id); return item.Id != 2; });
        Assert.True(changed);
        Assert.Equal(new uint[] { 3, 2, 1 }, sequence);
    }

    [Fact]
    public void ItemTargetsAreSeparateFromMovers()
    {
        var mover = Item(1, 1, 1);
        var target = Item(2, 6, 7);
        var requests = new List<(uint, int, int)>();
        Assert.True(Execute("wf_act_furni_to_furni", new() { IntParams = [100, 100] },
            [mover], [target], [], (item, x, y, _, _) => { requests.Add((item.Id, x, y)); return true; }));
        Assert.Equal(new[] { (1u, 6, 7) }, requests);
    }

    [Fact]
    public void AltitudeUsesDecimalTextAndLegacyOperatorOrdering()
    {
        var mover = Item(1, 1, 1);
        mover.GetZ = 2.5;
        double? requestedHeight = null;
        Assert.True(Execute("wf_act_set_altitude", new() { IntParams = [0, 100], Text = "1.25" },
            [mover], [], [], (_, _, _, _, height) => { requestedHeight = height; return true; }));
        Assert.Equal(3.75, requestedHeight);
        Assert.False(WiredRoomOperations.TryAltitude("1,25", out _));
        Assert.False(WiredRoomOperations.TryAltitude("NaN", out _));
    }

    [Fact]
    public void RotationWithoutMovementStaysOnTheSameTile()
    {
        (int x, int y, int rotation)? requested = null;
        Assert.True(Execute("wf_act_move_rotate", new() { IntParams = [0, 1, 100] },
            [Item(1, 5, 5)], [], [], (_, x, y, rotation, _) => { requested = (x, y, rotation); return true; }));
        Assert.Equal((5, 5, 2), requested);
    }

    [Fact]
    public void FootprintIncludesOriginAndRotatesTheWholeMultiTileItem()
    {
        var item = Item(1, 0, 0);
        item.Definition.Width = 2;
        item.Definition.Length = 3;
        Assert.Equal(6, WiredRoomOperations.Footprint(item, 4, 5, 0).Distinct().Count());
        Assert.Contains(new Point(5, 7), WiredRoomOperations.Footprint(item, 4, 5, 0));
        Assert.Contains(new Point(6, 6), WiredRoomOperations.Footprint(item, 4, 5, 2));
        Assert.DoesNotContain(new Point(5, 7), WiredRoomOperations.Footprint(item, 4, 5, 2));
    }

    [Fact]
    public void PlacementRejectsNegativeAndRotatedOutOfBoundsFootprintsBeforeMutation()
    {
        var (room, _, items) = RoomQueries();
        var item = Item(1, 0, 0);
        item.Definition.Length = 2;
        items.TryAdd(item.Id, item);
        Assert.False(WiredRoomOperations.CanMoveItem(room, item, -1, 0, 0));
        Assert.False(WiredRoomOperations.CanMoveItem(room, item, 2, 1, 2));
        Assert.True(WiredRoomOperations.CanMoveItem(room, item, 1, 1, 2));
        Assert.Equal((0, 0), (item.GetX, item.GetY));
    }

    [Fact]
    public void PlacementRejectsNonStackableFurnitureAndOccupiedCells()
    {
        var (room, map, items) = RoomQueries();
        var mover = Item(1, 0, 0);
        var blocker = Item(2, 1, 1);
        items.TryAdd(mover.Id, mover);
        items.TryAdd(blocker.Id, blocker);
        map.AddCoordinatedItem(blocker, blocker.Coordinate);
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0));
        blocker.Definition.Stackable = true;
        Assert.True(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0));
        map.AddUserToMap(new RoomUser(5, 1, 5, room, null), new(1, 1));
        Assert.False(WiredRoomOperations.CanMoveItem(room, mover, 1, 1, 0));
    }

    [Fact]
    public void SnapshotCapturesDefinitionAndComparesOnlyRequestedFields()
    {
        var item = Item(1, 3, 4);
        item.Definition.Id = 42;
        item.LegacyDataString = "on,with,separators";
        var snapshot = WiredRoomOperations.Capture(item);
        Assert.Equal(42u, snapshot.DefinitionId);
        Assert.Equal("on,with,separators", snapshot.State);
        item.GetX = 8;
        Assert.True(WiredRoomOperations.MatchesSnapshot(item, snapshot, true, true, false, true));
        Assert.False(WiredRoomOperations.MatchesSnapshot(item, snapshot, false, false, true, false));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public void EmptyQuantifiersFail(int quantifier, bool expected)
    {
        Assert.Equal(expected, WiredRoomOperations.Quantify([], quantifier));
    }

    [Fact]
    public void OneShotLongTimerDoesNotRepeatUntilRoomTimerReset()
    {
        var timers = new WiredTimedTriggers();
        var config = new WiredConfiguration { IntParams = [2] };
        Assert.False(timers.TryFire("wf_trg_at_time_long", 1, config, 9999, 9999, 0));
        Assert.True(timers.TryFire("wf_trg_at_time_long", 1, config, 10000, 10000, 0));
        Assert.False(timers.TryFire("wf_trg_at_time_long", 1, config, 20000, 20000, 0));
        Assert.True(timers.TryFire("wf_trg_at_time_long", 1, config, 30000, 10000, 1));
    }

    [Fact]
    public void ShortTimerUsesFiftyMillisecondUnitsAndDoesNotCatchUpInABurst()
    {
        var timers = new WiredTimedTriggers();
        var config = new WiredConfiguration { IntParams = [1] };
        Assert.False(timers.TryFire("wf_trg_period_short", 1, config, 0, 0, 0));
        Assert.False(timers.TryFire("wf_trg_period_short", 1, config, 49, 49, 0));
        Assert.True(timers.TryFire("wf_trg_period_short", 1, config, 50, 50, 0));
        Assert.True(timers.TryFire("wf_trg_period_short", 1, config, 5000, 5000, 0));
        Assert.False(timers.TryFire("wf_trg_period_short", 1, config, 5000, 5000, 0));
    }

    [Fact]
    public void ScoreTriggerFiresOnACrossingWithMatchingTeam()
    {
        var config = new WiredConfiguration { IntParams = [10, 2] };
        Assert.True(WiredTriggerPredicates.MatchesScore(config, 2, 8, 11));
        Assert.False(WiredTriggerPredicates.MatchesScore(config, 2, 11, 12));
        Assert.False(WiredTriggerPredicates.MatchesScore(config, 3, 8, 11));
    }

    [Fact]
    public void ChatMatchAndHideFieldsUseExistingEditorOrder()
    {
        var config = new WiredConfiguration { IntParams = [1, 1, 1], Text = "hello" };
        Assert.True(WiredTriggerPredicates.MatchesChat(config, "HELLO", true));
        Assert.False(WiredTriggerPredicates.MatchesChat(config, "HELLO", false));
        Assert.False(WiredTriggerPredicates.MatchesChat(config, "hello world", true));
        Assert.True(WiredTriggerPredicates.HidesChat(config));
    }

    [Fact]
    public void ActionFiltersUsePolarisSignAndDanceIds()
    {
        Assert.True(WiredTriggerPredicates.MatchesAction(new() { IntParams = [9, 1, 3, 0, 0] }, 9, 3));
        Assert.False(WiredTriggerPredicates.MatchesAction(new() { IntParams = [9, 1, 3, 0, 0] }, 9, 4));
        Assert.True(WiredTriggerPredicates.MatchesAction(new() { IntParams = [10, 0, 0, 1, 2] }, 10, 2));
    }

    [Fact]
    public void CalendarRangesWrapMidnightAndUseMondayWeekdayBits()
    {
        var config = new WiredConfiguration { IntParams = [2, 22, 2, 0, 0, 0, 0, 0, 0] };
        Assert.True(WiredTimeConditions.MatchesTime(config, new(2026, 10, 2, 23, 0, 0, TimeSpan.Zero)));
        Assert.True(WiredTimeConditions.MatchesTime(config, new(2026, 10, 2, 1, 0, 0, TimeSpan.Zero)));
        Assert.False(WiredTimeConditions.MatchesTime(config, new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero)));
        var weekdays = new WiredConfiguration { IntParams = [1 << 4, 0, 1, 31, 1 << 9, 0, 0, 9999] };
        Assert.True(WiredTimeConditions.MatchesDate(weekdays, new(2026, 10, 2, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(WiredTimeConditions.MatchesDate(weekdays, new(2026, 10, 3, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void ValidMovesRequiresANeighbourForEverySelectedItemAndDoesNotMoveThem()
    {
        var first = Item(1, 1, 1);
        var second = Item(2, 2, 2);
        Assert.False(WiredItemConditions.ValidMoves([first, second], (item, _, _) => item.Id == 1));
        Assert.True(WiredItemConditions.ValidMoves([first, second], (item, x, y) => x == item.GetX + 1 && y == item.GetY));
        Assert.Equal((1, 1), (first.GetX, first.GetY));
        Assert.Equal((2, 2), (second.GetX, second.GetY));
        Assert.False(WiredItemConditions.ValidMoves([], (_, _, _) => true));
    }

    [Fact]
    public void TypeConditionComparesResolvedRolesWithoutMixingThem()
    {
        var selected = Item(1, 0, 0);
        selected.Definition.Id = 10;
        var other = Item(2, 0, 0);
        other.Definition.Id = 20;
        Assert.False(WiredItemConditions.MatchesType(new() { IntParams = [100, 201, 0] }, [selected], [other]));
        other.Definition.Id = 10;
        Assert.True(WiredItemConditions.MatchesType(new() { IntParams = [100, 201, 0] }, [selected], [other]));
        Assert.False(WiredItemConditions.MatchesType(new() { IntParams = [100, 201, 1] }, [selected], []));
    }

    private static Item Item(uint id, int x, int y) => new()
    {
        Id = id, GetX = x, GetY = y, ExtraData = new LegacyDataFormat { Data = "0" },
        Definition = new() { Type = ItemType.Floor, Width = 1, Length = 1, Modes = 2,
            AdjustableHeights = [], VendingIds = [], ItemName = "test", PublicName = "test" }
    };

    private static (Room room, Gamemap map, ConcurrentDictionary<uint, Item> items) RoomQueries()
    {
        // Initialise the real query modules without Room's database-loading constructor.
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var model = new RoomModel("wired-test", 0, 0, 0, 0, "000\r000\r000", 0, 0, true);
        var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty);
        var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomItemHandling", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, handler);
        var items = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling)
            .GetField("_floorItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(handler)!;
        return (room, map, items);
    }

    private static bool Execute(string name, WiredConfiguration config, Item[] movers, Item[] targets,
        RoomUser[] users, WiredMovementActions.MoveFurniture move) =>
        new WiredMovementActions().Execute(name, config, movers, targets, users, move,
            (_, _, _, _, _) => false, (_, _) => { });
}
