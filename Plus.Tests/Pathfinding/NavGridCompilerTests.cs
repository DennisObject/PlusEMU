using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

[Collection("Pathfinding room adapter")]
public class NavGridCompilerTests
{
    [Fact]
    public void NonWalkableEffectIsBlockedInCompatibilityMode()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 1);
        inputs.Publish(NavTest.Record(10, 1, [1], walkable: false, interaction: InteractionType.Effect));
        compiler.ApplyNow();
        Assert.False(grid.Active(1));
        Assert.False(new MovementRules(grid, new()).CanStep(new(), new(0, 0, 0),
            grid.Position(1), StepPurpose.Goal, OccupancyView.Execution).Ok);
    }

    [Fact]
    public void CompatibilityCorpusUsesEffectiveKindAndDeterministicTopOrder()
    {
        var (grid, inputs, compiler) = NavTest.Create(9, 1, states: [SquareState.Open, SquareState.Open, SquareState.Open,
            SquareState.Seat, SquareState.Blocked, SquareState.Open, SquareState.Open, SquareState.Open, SquareState.Blocked], door: 4);
        inputs.Publish(NavTest.Record(1, 1, [0], walkable: false));
        inputs.Publish(NavTest.Record(2, 2, [1])); inputs.Publish(NavTest.Record(3, 3, [1]));
        inputs.Publish(NavTest.Record(4, 4, [2], z: 2, h: 3, seat: true));
        inputs.Publish(NavTest.Record(5, 5, [5], h: 0.5, walkable: false, interaction: InteractionType.GuildGate));
        inputs.Publish(NavTest.Record(6, 6, [6], h: 0.5, walkable: false, interaction: InteractionType.GuildGate, state: "1"));
        inputs.Publish(NavTest.Record(7, 7, [7], h: 1, walkable: false, interaction: InteractionType.Gate, state: "1"));
        inputs.Publish(NavTest.Record(8, 8, [8], z: 2, h: 1)); compiler.ApplyNow();
        Assert.False(grid.Active(0)); Assert.False(grid.TileVoid[0]); // Furniture wall on non-void terrain.
        Assert.True(grid.Active(1)); Assert.Equal((uint)3, grid.SupportItem[1]);
        Assert.Equal(2, grid.WalkZ[2]); Assert.Equal(NavFlags.GoalOnlySeat, grid.Flags[2]);
        Assert.Equal(NavFlags.GoalOnlySeat | NavFlags.ModelSeat, grid.Flags[3]);
        Assert.Equal(NavFlags.Door, grid.Flags[4]); Assert.False(grid.TileVoid[4]);
        Assert.Equal(NavFlags.Transit | NavFlags.GuildGate, grid.Flags[5]); Assert.Equal(grid.Flags[5], grid.Flags[6]);
        Assert.Equal(NavFlags.Transit, grid.Flags[7]); Assert.Equal(3, grid.WalkZ[8]); Assert.False(grid.TileVoid[8]);
    }

    [Fact]
    public void AdjustableSeatAndDirectStateHeightChangesPublishRecords()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 1);
        var item = NavTest.Item(); item.GetX = 1; item.GetZ = 1;
        item.Definition.IsSeat = true; item.Definition.Height = 1; item.Definition.AdjustableHeights = [1, 3];
        item.ExtraData = FurniExtraData.Load(item.Definition, "0", true);
        inputs.Attach(item); compiler.ApplyNow();
        item.LegacyDataString = "1"; compiler.ApplyNow();
        Assert.Equal(3, inputs.AppliedRecords[1].Height); Assert.Equal(1, grid.WalkZ[1]);
        item.GetZ = 1.5004; compiler.ApplyNow(); Assert.Equal(1.5004, grid.WalkZ[1]);
        item.Rotation = 2; compiler.ApplyNow(); Assert.Equal(2, inputs.AppliedRecords[1].Rotation);
    }

    [Fact]
    public void LegacyHeightAndTileStateParityOnUnambiguousStacks()
    {
        // Tie order, adjustable-seat height, guild access and raised open gates have explicit
        // v2 policies. This parity corpus uses stacks where legacy has an unambiguous result.
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("parity", 0, 0, 0, 0, "000\r000\r000", 0, 0, false), TestLogging.Navigation);
        Set(room, "_gamemap", map); Set(room, "_roomUserManager", new RoomUserManager(room));
        var handler = new RoomItemHandling(room); Set(room, "_roomItemHandling", handler);
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;
        foreach (var (height, seat, walk, interaction, state) in new[] {
            (0.0, false, false, InteractionType.None, "0"),
            (0.0, false, true, InteractionType.None, "0"),
            (1.25, false, true, InteractionType.None, "0"),
            (1.0, true, false, InteractionType.None, "0"),
            (1.0, false, false, InteractionType.Bed, "0"),
            (0.5, false, false, InteractionType.Gate, "0"),
            (0.5, false, false, InteractionType.Gate, "1") })
        {
            floor.Clear();
            var item = NavTest.Item(); item.GetX = item.GetY = 1; item.Definition.Height = height;
            item.Definition.IsSeat = seat; item.Definition.Walkable = walk; item.Definition.InteractionType = interaction;
            item.ExtraData = FurniExtraData.Load(item.Definition, state, true);
            item.SetState(1, 1, 0, Gamemap.GetAffectedTiles(1, 1, 1, 1, 0)); floor[item.Id] = item;
            map.GenerateMaps();
            var (grid, inputs, compiler) = NavTest.Create(3, 3, door: 0);
            inputs.Attach(item); compiler.ApplyNow();
            Assert.Equal(map.SqAbsoluteHeight(1, 1, [item]), grid.WalkZ[4]);
            Assert.Equal(map.GameMap[1, 1] != 0, grid.Active(4));
            Assert.Equal(map.GameMap[1, 1] == 3, (grid.Flags[4] & (NavFlags.GoalOnlySeat | NavFlags.GoalOnlyBed)) != 0);
        }
    }

    [Fact]
    public void DirtyRebuildMatchesFullRebuildAndInputOrderDoesNotMatter()
    {
        var (grid, inputs, compiler) = NavTest.Create(12, 12);
        var random = new Random(173);
        for (var i = 1; i <= 500; i++)
        {
            var id = (uint)random.Next(1, 21);
            var record = NavTest.Record(id, i, [random.Next(144), random.Next(144)], z: random.Next(5), h: random.Next(3), walkable: random.Next(3) != 0, seat: random.Next(5) == 0);
            if (random.Next(8) == 0) record = record with { Removed = true };
            inputs.Publish(record); compiler.ApplyNow();
            var z = grid.WalkZ.ToArray(); var flags = grid.Flags.ToArray(); var supports = grid.SupportItem.ToArray();
            compiler.RebuildAll(); Assert.Equal(z, grid.WalkZ); Assert.Equal(flags, grid.Flags); Assert.Equal(supports, grid.SupportItem);
        }
        var other = NavTest.Create(12, 12);
        foreach (var record in inputs.AppliedRecords.Values.Reverse()) other.Inputs.Publish(record);
        other.Compiler.RebuildAll(); Assert.Equal(grid.WalkZ, other.Grid.WalkZ); Assert.Equal(grid.SupportItem, other.Grid.SupportItem);
    }

    [Fact]
    public void BaseTerrainDoesNotReadLegacyOpenSquareAndLocksReachEverySurface()
    {
        var states = new[] { SquareState.Open, SquareState.Blocked };
        var heights = new double[] { 0, 0 };
        var (grid, inputs, compiler) = NavTest.Create(2, 1, z: heights, states: states);
        states[1] = SquareState.Open; heights[1] = 9; compiler.RebuildAll();
        Assert.True(grid.TileVoid[1]); Assert.Equal(0, grid.WalkZ[1]);
        inputs.Publish(NavTest.Record(1, 1, [1], z: 1.5004)); grid.FloorLocks[1] = 1; compiler.ApplyNow();
        Assert.False(grid.TileVoid[1]); Assert.Equal(NavFlags.Transit | NavFlags.FloorLocked, grid.Flags[1]);
    }

    [Fact]
    public void StacktoolCompatibilitySwitchIsExplicit()
    {
        foreach (var collision in new[] { true, false })
        {
            var settings = new PathfindingSettings { StacktoolLegacyCollision = collision };
            var (grid, inputs, compiler) = NavTest.Create(2, 1, settings);
            inputs.Publish(NavTest.Record(1, 1, [1], z: 3, h: 1, walkable: false, interaction: InteractionType.Stacktool)); compiler.ApplyNow();
            Assert.Equal(!collision, grid.Active(1)); Assert.Equal(collision ? 4 : 0, grid.LegacyZ[1]);
        }
    }

    [Fact]
    public void WalkMagicCompilesOneSurfaceByBaseHeightThenIdAndPreservesDoorsAndLocks()
    {
        var (grid, inputs, compiler) = NavTest.Create(7, 1, states: [SquareState.Open,
            SquareState.Blocked, SquareState.Seat, SquareState.Open, SquareState.Open,
            SquareState.Open, SquareState.Blocked], door: 6);
        inputs.Publish(NavTest.Record(1, 1, [0, 1, 2, 3, 4, 5, 6], z: 9, h: 8, walkable: false));
        inputs.Publish(NavTest.Record(2, 2, [2], z: 10, h: 2, seat: true));
        inputs.Publish(NavTest.Record(3, 3, [3], z: 10, h: 2, interaction: InteractionType.Bed));
        inputs.Publish(NavTest.Record(4, 4, [4], z: 20, walkable: false, interaction: InteractionType.GuildGate));
        inputs.Publish(NavTest.Record(5, 5, [5], z: 20, walkable: false, interaction: InteractionType.Gate));
        var low = NavTest.Record(100, 6, [0, 1, 2, 3, 4, 5, 6], z: 1, h: 100,
            walkable: false, interaction: InteractionType.WalkMagicTile);
        var high = low with { ItemId = 10, Version = 7, Z = 1.5004, Height = 0 };
        var tie = high with { ItemId = 11, Version = 8 };
        inputs.Publish(high); inputs.Publish(tie); inputs.Publish(low);
        grid.FloorLocks[5] = 1; compiler.ApplyNow();
        for (var tile = 0; tile < 6; tile++)
        {
            Assert.Equal(SurfaceKind.WalkMagic, grid.Kind[tile]);
            Assert.Equal(NavFlags.Transit | (tile == 5 ? NavFlags.FloorLocked : NavFlags.None), grid.Flags[tile]);
            Assert.Equal(1.5004, grid.WalkZ[tile]); Assert.Equal(grid.WalkZ[tile], grid.LegacyZ[tile]);
            Assert.Equal((uint)11, grid.SupportItem[tile]); Assert.False(grid.TileVoid[tile]);
            Assert.Empty(grid.PillowTiles[tile]);
        }
        Assert.Equal(SurfaceKind.Door, grid.Kind[6]); Assert.Equal(NavFlags.Door, grid.Flags[6]);
        Assert.Equal(7, grid.ActiveNodeCount); Assert.Equal(7, grid.SlotCapacity);
        inputs.Publish(tie with { Version = 9, Removed = true }); compiler.ApplyNow();
        Assert.Equal((uint)10, grid.SupportItem[1]);
        inputs.Publish(high with { Version = 10, Removed = true }); compiler.ApplyNow();
        Assert.Equal(1, grid.WalkZ[1]); Assert.Equal((uint)100, grid.SupportItem[1]);
    }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
}
