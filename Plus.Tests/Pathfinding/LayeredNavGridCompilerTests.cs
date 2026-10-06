using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class LayeredNavGridCompilerTests
{
    [Theory]
    [InlineData(1.5, true)]
    [InlineData(1.499, false)]
    public void ClearanceFilterUsesHalfOpenIntervals(double blockerZ, bool floorKept)
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: blockerZ, h: 1, walkable: false));
        compiler.ApplyNow();
        Assert.Equal(floorKept ? [(0d, NavFlags.Transit, 0u, SurfaceKind.Floor)] : Array.Empty<(double, NavFlags, uint, SurfaceKind)>(),
            Surfaces(grid, 1));
    }

    [Fact]
    public void ZeroHeightBlockerUsesEpsilonButZeroHeightWalkableNeverObstructs()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: 1.4995, walkable: false));
        inputs.Publish(NavTest.Record(11, 2, [2], z: 1.4995));
        compiler.ApplyNow();
        Assert.Empty(Surfaces(grid, 1));
        Assert.Equal([(0d, NavFlags.Transit, 0u, SurfaceKind.Floor), (1.4995, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 2));
    }

    [Fact]
    public void BridgeOverFloorCompilesTwoOrderedSurfacesWithAnOverflowSlot()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: 2, h: 0.5));
        compiler.ApplyNow();
        Assert.True(grid.Layered);
        Assert.Equal([(0d, NavFlags.Transit, 0u, SurfaceKind.Floor), (2.5, NavFlags.Transit, 10u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Equal(1, grid.SurfaceAt(1, 0));
        Assert.Equal(3, grid.SurfaceAt(1, 1));
        Assert.Equal(1, grid.TileOf(3));
        Assert.Equal(1, grid.Ordinal[3]);
        Assert.Equal(0, grid.Ordinal[1]);
        Assert.Equal(4, grid.SlotCapacity);
        Assert.Equal(4, grid.ActiveNodeCount);
        Assert.Equal(new SurfaceRef(1, 10, SurfaceKind.Top), grid.Reference(3));
        Assert.Equal(3, grid.SlotOf(new SurfaceRef(1, 10, SurfaceKind.Top)));
        Assert.Equal(1, grid.SlotOf(new SurfaceRef(1, 0, SurfaceKind.Floor)));
        Assert.Equal(new NavPosition(1, 0, 2.5, 3), grid.Position(3));
        Assert.False(grid.TileVoid[1]);
    }

    [Fact]
    public void SeatUnderARaisedBlockerKeepsTheSeatOnlyInLayeringMode()
    {
        foreach (var layered in new[] { true, false })
        {
            var (grid, inputs, compiler) = layered ? Layered() : NavTest.Create(3, 1);
            inputs.Publish(NavTest.Record(10, 1, [1], h: 1, walkable: false, seat: true));
            inputs.Publish(NavTest.Record(11, 2, [1], z: 2, h: 1, walkable: false));
            compiler.ApplyNow();

            if (layered)
            {
                Assert.Equal([(0d, NavFlags.GoalOnlySeat, 10u, SurfaceKind.SeatBase)], Surfaces(grid, 1));
            }
            else
            {
                Assert.False(grid.Active(1));
            }
        }
    }

    [Fact]
    public void WalkableItemDirectlyOnABlockerIsTheOnlySurface()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], h: 1, walkable: false));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 1, h: 0.5));
        compiler.ApplyNow();
        Assert.Equal([(1.5, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Equal(new uint[] { 10, 11 }, Contacts(grid, 1, 0));
    }

    [Fact]
    public void CoincidentFloorAndRugsCoalesceIntoOneSurfaceWithBothContacts()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1]));
        inputs.Publish(NavTest.Record(11, 2, [1]));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Equal(new uint[] { 10, 11 }, Contacts(grid, 1, 0));
        Assert.Equal(3, grid.ActiveNodeCount);
        Assert.Equal(3, grid.SlotCapacity);
    }

    [Fact]
    public void SupportsAtDistinctHeightsAreNotMergedAndTheExactClimbStaysLegal()
    {
        var (grid, inputs, compiler) = Layered(k: 3);
        inputs.Publish(NavTest.Record(10, 1, [1], z: 1.5));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 1.5004));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.Transit, 0u, SurfaceKind.Floor), (1.5, NavFlags.Transit, 10u, SurfaceKind.Top),
            (1.5004, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 1));
        var rules = new MovementRules(grid, new());
        Assert.True(rules.CanStep(new(), grid.Position(0), grid.Position(grid.SurfaceAt(1, 1)), StepPurpose.Goal, OccupancyView.Execution).Ok);
        Assert.Equal(StepReason.TooHigh, rules.CanStep(new(), grid.Position(0), grid.Position(grid.SurfaceAt(1, 2)),
            StepPurpose.Goal, OccupancyView.Execution).Reason);
    }

    [Fact]
    public void CoalescedAccessRolesFollowPrecedenceAndHigherItemIds()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(11, 1, [0]));
        inputs.Publish(NavTest.Record(10, 2, [0], walkable: false, seat: true));
        inputs.Publish(NavTest.Record(13, 3, [1]));
        inputs.Publish(NavTest.Record(12, 4, [1], walkable: false, interaction: InteractionType.GuildGate, group: 9));
        inputs.Publish(NavTest.Record(14, 5, [2]));
        inputs.Publish(NavTest.Record(15, 6, [2]));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.GoalOnlySeat, 10u, SurfaceKind.SeatBase)], Surfaces(grid, 0));
        Assert.Equal([(0d, NavFlags.Transit | NavFlags.GuildGate, 12u, SurfaceKind.GateBase)], Surfaces(grid, 1));
        Assert.Equal(9, grid.GroupId[grid.SurfaceAt(1, 0)]);
        Assert.Equal([(0d, NavFlags.Transit, 15u, SurfaceKind.Top)], Surfaces(grid, 2));
        Assert.Equal(new uint[] { 10, 11 }, Contacts(grid, 0, 0));
    }

    [Fact]
    public void ShuffledInputOrderCompilesIdentically()
    {
        NavItemRecord[] records =
        [
            NavTest.Record(10, 1, [1]), NavTest.Record(11, 1, [1], z: 2, h: 0.5), NavTest.Record(12, 1, [1], z: 2.5),
            NavTest.Record(13, 1, [1], z: 2.5, walkable: false, seat: true), NavTest.Record(14, 1, [1], z: 5, h: 1, walkable: false)
        ];
        string? expected = null;
        var random = new Random(6);

        for (var run = 0; run < 24; run++)
        {
            var (grid, inputs, compiler) = Layered(k: 4);
            var version = 1;

            foreach (var record in records.OrderBy(_ => random.Next()))
            {
                inputs.Publish(record with
                {
                    Version = version++
                });
            }

            compiler.ApplyNow();
            var compiled = string.Join("|", Enumerable.Range(0, grid.SurfaceCount(1)).Select(o =>
                $"{Surfaces(grid, 1)[o]}:{string.Join(",", Contacts(grid, 1, o))}"));
            expected ??= compiled;
            Assert.Equal(expected, compiled);
        }

        Assert.Equal("(0, Transit, 10, Top):10|(2.5, GoalOnlySeat, 13, SeatBase):11,12,13,14", expected);
    }

    [Fact]
    public void CoalescingRunsBeforeTheCapAndTheCapKeepsTheHighest()
    {
        var (grid, inputs, compiler) = Layered(k: 2);
        inputs.Publish(NavTest.Record(10, 1, [1]));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 2));
        inputs.Publish(NavTest.Record(12, 3, [2], z: 2));
        inputs.Publish(NavTest.Record(13, 4, [2], z: 4));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.Transit, 10u, SurfaceKind.Top), (2d, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Equal([(2d, NavFlags.Transit, 12u, SurfaceKind.Top), (4d, NavFlags.Transit, 13u, SurfaceKind.Top)], Surfaces(grid, 2));
    }

    [Fact]
    public void OverflowCapKeepsPinnedSurfacesBeforeTheHighest()
    {
        foreach (var pinFloor in new[] { true, false })
        {
            var (grid, inputs, compiler) = Layered(k: 2);
            inputs.Publish(NavTest.Record(10, 1, [1], z: 2));
            compiler.ApplyNow();
            compiler.SurfacePinned = surface => pinFloor && surface == new SurfaceRef(1, 0, SurfaceKind.Floor);
            inputs.Publish(NavTest.Record(11, 2, [1], z: 4));
            compiler.ApplyNow();
            Assert.Equal(pinFloor ? [0d, 4d] : [2d, 4d], Surfaces(grid, 1).Select(s => s.Z));

            if (pinFloor)
            {
                Assert.Equal(1, grid.SlotOf(new SurfaceRef(1, 0, SurfaceKind.Floor)));
            }

            Assert.Empty(grid.ForcedOffGraph);
        }
    }

    [Fact]
    public void MoreThanFourPinnedSurfacesKeepTheHighestFourAndReportTheLowestOffGraph()
    {
        var (grid, inputs, compiler) = Layered(k: 2);
        compiler.SurfacePinned = _ => true;

        for (uint id = 10; id < 14; id++)
        {
            inputs.Publish(NavTest.Record(id, id, [1], z: (id - 9) * 2));
        }

        compiler.ApplyNow();
        Assert.Equal([2d, 4d, 6d, 8d], Surfaces(grid, 1).Select(s => s.Z));
        Assert.Equal(new[] { new SurfaceRef(1, 0, SurfaceKind.Floor) }, grid.ForcedOffGraph);
    }

    [Fact]
    public void OverflowSlotsAreStableReuseFreedHolesAndNeverCompact()
    {
        var (grid, inputs, compiler) = Layered(w: 4);

        for (uint id = 10; id < 13; id++)
        {
            inputs.Publish(NavTest.Record(id, id, [(int)id - 10], z: 2));
        }

        compiler.ApplyNow();
        Assert.Equal([4, 5, 6], new[] { 0, 1, 2 }.Select(t => grid.SurfaceAt(t, 1)));
        Assert.Equal(7, grid.SlotCapacity);
        Assert.Equal(7, grid.ActiveNodeCount);
        inputs.Publish(NavTest.Record(11, 20, [1], z: 2, removed: true));
        compiler.ApplyNow();
        Assert.Equal(1, grid.SurfaceCount(1));
        Assert.False(grid.Active(5));
        Assert.Equal(7, grid.SlotCapacity);
        Assert.Equal(6, grid.ActiveNodeCount);
        Assert.Equal((4, 6), (grid.SurfaceAt(0, 1), grid.SurfaceAt(2, 1)));
        inputs.Publish(NavTest.Record(13, 21, [3], z: 2));
        compiler.ApplyNow();
        Assert.Equal(5, grid.SurfaceAt(3, 1));
        Assert.Equal(3, grid.TileOf(5));
        Assert.Equal(7, grid.SlotCapacity);
        inputs.Publish(NavTest.Record(10, 22, [0], z: 3));
        compiler.ApplyNow();
        Assert.Equal(4, grid.SlotOf(new SurfaceRef(0, 10, SurfaceKind.Top)));
        Assert.Equal(3, grid.WalkZ[4]);
        Assert.Equal(7, grid.ActiveNodeCount);
    }

    [Fact]
    public void RebuildNeverRemapsSlotsOutsideTheDirtySet()
    {
        var (grid, inputs, compiler) = Layered(w: 4, k: 4);

        for (uint id = 10; id < 14; id++)
        {
            inputs.Publish(NavTest.Record(id, id, [(int)id - 10], z: 2));
        }

        inputs.Publish(NavTest.Record(20, 20, [0], z: 4));
        inputs.Publish(NavTest.Record(21, 21, [3], z: 4));
        compiler.ApplyNow();
        var before = Snapshot(grid, 0, 2, 3);
        inputs.Publish(NavTest.Record(11, 30, [1], z: 2, removed: true));
        inputs.Publish(NavTest.Record(22, 31, [1], z: 4));
        inputs.Publish(NavTest.Record(23, 32, [1], z: 6));
        compiler.ApplyNow();
        Assert.Equal(before, Snapshot(grid, 0, 2, 3));
        Assert.Equal([0d, 4d, 6d], Surfaces(grid, 1).Select(s => s.Z));
    }

    [Fact]
    public void ClosedNonWalkableGuildGateBlocksTheFloorBelowForNonMembers()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: 0.5, walkable: false, interaction: InteractionType.GuildGate, state: "0", group: 9));
        compiler.ApplyNow();
        Assert.Equal([(0.5, NavFlags.Transit | NavFlags.GuildGate, 10u, SurfaceKind.GateBase)], Surfaces(grid, 1));
        var rules = new MovementRules(grid, new());
        var member = new ActorProfile();
        member.SetMembership(9, true);

        foreach (var slot in Enumerable.Range(0, grid.SurfaceCount(1)).Select(o => grid.SurfaceAt(1, o)))
        {
            Assert.Equal(StepReason.GateDenied, rules.CanStep(new(), grid.Position(0), grid.Position(slot), StepPurpose.Goal, OccupancyView.Execution).Reason);
        }

        Assert.True(rules.CanStep(member, grid.Position(0), grid.Position(grid.SurfaceAt(1, 0)), StepPurpose.Goal, OccupancyView.Execution).Ok);
    }

    [Fact]
    public void WalkMagicTileForcesASingleSurface()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: 2, h: 0.5));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 1, interaction: InteractionType.WalkMagicTile));
        compiler.ApplyNow();
        Assert.Equal([(1d, NavFlags.Transit, 11u, SurfaceKind.WalkMagic)], Surfaces(grid, 1));
        Assert.Equal(new uint[] { 10, 11 }, Contacts(grid, 1, 0));
    }

    [Fact]
    public void DoorTileKeepsItsSingleDoorSurface()
    {
        var (grid, inputs, compiler) = Layered(door: 1);
        inputs.Publish(NavTest.Record(10, 1, [1], z: 2));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.Door, 0u, SurfaceKind.Door)], Surfaces(grid, 1));
    }

    [Fact]
    public void LayeringOffKeepsTheCompatibilitySurface()
    {
        var (grid, inputs, compiler) = NavTest.Create(3, 1);
        inputs.Publish(NavTest.Record(10, 1, [1], z: 2, h: 0.5));
        compiler.ApplyNow();
        Assert.False(grid.Layered);
        Assert.Equal(1, grid.SurfaceCount(1));
        Assert.Equal(1, grid.SurfaceAt(1, 0));
        Assert.Equal((2.5, NavFlags.Transit), (grid.WalkZ[1], grid.Flags[1]));
        Assert.Equal(3, grid.SlotCapacity);
    }

    [Theory]
    [InlineData(InteractionType.Banzaifloor, WiredBoxType.None, false)]
    [InlineData(InteractionType.FreezeTile, WiredBoxType.None, false)]
    [InlineData(InteractionType.Football, WiredBoxType.None, false)]
    [InlineData(InteractionType.WiredEffect, WiredBoxType.EffectShowMessage, false)]
    [InlineData(InteractionType.WiredCondition, WiredBoxType.ConditionFurniHasUsers, false)]
    [InlineData(InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOnFurni, true)]
    [InlineData(InteractionType.WiredTrigger, WiredBoxType.TriggerWalkOffFurni, true)]
    [InlineData(InteractionType.Effect, WiredBoxType.None, true)]
    public void UnmigratedGameOrWiredFurnitureKeepsTheRoomAtOneSurface(InteractionType interaction, WiredBoxType wired, bool layered)
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], z: 2, h: 0.5));
        inputs.Publish(NavTest.Record(11, 2, [2], walkable: false, interaction: interaction) with
        {
            WiredType = wired
        });
        compiler.ApplyNow();
        Assert.Equal(layered, grid.Layered);
        Assert.Equal(layered ? 2 : 1, grid.SurfaceCount(1));
        inputs.Publish(NavTest.Record(11, 3, [2], walkable: false, interaction: interaction, removed: true) with
        {
            WiredType = wired
        });
        compiler.ApplyNow();
        Assert.True(grid.Layered);
        Assert.Equal([0d, 2.5], Surfaces(grid, 1).Select(s => s.Z));
    }

    [Fact]
    public void LowerSeatEndingAtBridgeHeightBelongsToTheLowerLayer()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(10, 1, [1], h: 2, walkable: false, seat: true));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 2));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.GoalOnlySeat, 10u, SurfaceKind.SeatBase), (2d, NavFlags.Transit, 11u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Equal(new uint[] { 10 }, Contacts(grid, 1, 0));
        Assert.Equal(new uint[] { 11 }, Contacts(grid, 1, 1));
        Assert.Equal(grid.SurfaceAt(1, 0), grid.OwnerOf(1, 10));
    }

    [Fact]
    public void RugAtBridgeHeightBelongsToTheBridge()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(11, 1, [1], z: 1.5, h: 0.5));
        inputs.Publish(NavTest.Record(12, 2, [1], z: 2));
        compiler.ApplyNow();
        Assert.Equal([(0d, NavFlags.Transit, 0u, SurfaceKind.Floor), (2d, NavFlags.Transit, 12u, SurfaceKind.Top)], Surfaces(grid, 1));
        Assert.Empty(Contacts(grid, 1, 0));
        Assert.Equal(new uint[] { 11, 12 }, Contacts(grid, 1, 1));
    }

    [Fact]
    public void ItemsWithoutTheirOwnSurfaceAreOwnedByTheSurfaceTheyRestOn()
    {
        var (grid, inputs, compiler) = Layered();
        inputs.Publish(NavTest.Record(11, 1, [1], z: 2));
        inputs.Publish(NavTest.Record(12, 2, [1], z: 4, h: 1, walkable: false));
        inputs.Publish(NavTest.Record(13, 3, [1], z: 3.6, walkable: false));
        compiler.ApplyNow();
        Assert.Equal([0d, 2d], Surfaces(grid, 1).Select(s => s.Z));
        Assert.Equal(new uint[] { 11, 12, 13 }, Contacts(grid, 1, 1));
        Assert.Equal(grid.SurfaceAt(1, 1), grid.OwnerOf(1, 12));
        Assert.Equal(-1, grid.OwnerOf(1, 99));
    }

    private static (NavGrid Grid, NavInputs Inputs, NavGridCompiler Compiler) Layered(int w = 3, int k = 2, int door = -1)
        => NavTest.Create(w, 1, new PathfindingSettings { LayeringEnabled = true, MaxSurfacesPerTile = k }, door: door);

    internal static (double Z, NavFlags Flags, uint Support, SurfaceKind Kind)[] Surfaces(NavGrid grid, int tile)
        => Enumerable.Range(0, grid.SurfaceCount(tile)).Select(o => grid.SurfaceAt(tile, o))
            .Select(s => (grid.WalkZ[s], grid.Flags[s], grid.SupportItem[s], grid.Kind[s])).ToArray();

    private static uint[] Contacts(NavGrid grid, int tile, int ordinal) => grid.Contacts(grid.SurfaceAt(tile, ordinal)).ToArray().Order().ToArray();

    private static string Snapshot(NavGrid grid, params int[] tiles) => string.Join(";", tiles.Select(t => string.Join(",",
        Enumerable.Range(0, grid.SurfaceCount(t)).Select(o => grid.SurfaceAt(t, o)).Select(s => $"{s}:{grid.Reference(s)}:{grid.WalkZ[s]}"))));
}
