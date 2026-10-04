using System.Drawing;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

// Layered passes over the blocked-route fallback (S2) and gate-close occupancy (S4).
public class LayeredStackPassTests
{
    [Fact]
    public void ValidPrefixContinuesThroughAnotherSurfaceWhenTheOriginalSupportWasLowered()
    {
        // Current Z 2; the next tile's original support now sits at 0.1, another surface at 1.7; the tile after is at 3.
        var (grid, inputs, compiler) = NavTest.Create(3, 1, new PathfindingSettings { LayeringEnabled = true, MaxSurfacesPerTile = 4 });
        inputs.Publish(NavTest.Record(10, 1, [0], h: 2)); inputs.Publish(NavTest.Record(11, 2, [1], z: 0.1));
        inputs.Publish(NavTest.Record(12, 3, [1], z: 1.7)); inputs.Publish(NavTest.Record(13, 4, [2], h: 3));
        compiler.ApplyNow();
        RetainedStep[] steps = [new(1, 0, StepPurpose.Transit, 11, 2), new(2, 0, StepPurpose.Goal, 13, 3)];
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        var prefix = Find(grid, occupancy, steps);
        Assert.Equal([(1.7, 12u), (3d, 13u)], prefix.Select(step => (step.Z, step.SupportItem)));
        Assert.Equal(grid.SlotOf(new SurfaceRef(1, 12, SurfaceKind.Top)), prefix[0].Slot);
        occupancy.Targets[prefix[0].Slot] = TargetOccupancy.Stationary;
        prefix = Find(grid, occupancy, steps);
        Assert.Equal([(0.1, 11u)], prefix.Select(step => (step.Z, step.SupportItem)));
    }

    [Fact]
    public void GateCloseOccupancyCoversEverySurfaceOfTheFootprint()
    {
        var (grid, inputs, compiler) = NavTest.Create(3, 1, new PathfindingSettings { LayeringEnabled = true });
        inputs.Publish(NavTest.Record(10, 1, [1], h: 1, walkable: false, interaction: InteractionType.Gate, state: "1"));
        inputs.Publish(NavTest.Record(11, 2, [1], z: 2.6));
        compiler.ApplyNow();
        var deck = grid.SlotOf(new SurfaceRef(1, 11, SurfaceKind.Top));
        Assert.Equal((1, true), (grid.SurfaceAt(1, 0), deck >= grid.TileCount));
        var claims = new ClaimLedger(grid);
        var occupancy = new ExecutorGateOccupancy(grid, claims);
        var standing = Actor(); var claiming = Actor();
        claims.Move(standing, deck, 1, walking: false, groupId: 1);
        Assert.True(occupancy.IsBlocked([new Point(1, 0)]));
        claims.Move(standing, 0, 0, walking: false, groupId: 1);
        claims.Move(claiming, 2, 2, walking: false, groupId: 2);
        Assert.False(occupancy.IsBlocked([new Point(1, 0)]));
        Assert.True(claims.TryClaim(claiming, deck, ClaimKind.Roller, TargetOccupancy.None));
        Assert.True(occupancy.IsBlocked([new Point(1, 0)]));
    }

    [Fact]
    public void CargoReservationsAreTileWideAndSurviveSlotReuse()
    {
        // 4x4: a deck over tile 6 takes overflow slot 16; the deck later moves to tile 11 and reuses slot 16.
        var (grid, inputs, compiler) = NavTest.Create(4, 4, new PathfindingSettings { LayeringEnabled = true });
        inputs.Publish(NavTest.Record(21, 1, [6], z: 2)); compiler.ApplyNow();
        Assert.Equal(16, grid.SlotOf(new SurfaceRef(6, 21, SurfaceKind.Top)));
        var claims = new ClaimLedger(grid);
        Assert.True(claims.TryReserveCargo(6, TargetOccupancy.None, null));
        Assert.Equal(TargetOccupancy.RollerClaim, claims.OccupancyAt(16, 0));
        inputs.Publish(NavTest.Record(21, 2, [11], z: 2)); compiler.ApplyNow();
        claims.EnsureCapacity(grid.SlotCapacity);
        Assert.Equal(16, grid.SlotOf(new SurfaceRef(11, 21, SurfaceKind.Top)));
        Assert.Equal(TargetOccupancy.None, claims.OccupancyAt(16, 0));
        Assert.Equal(TargetOccupancy.RollerClaim, claims.OccupancyAt(6, 0));
        claims.ReleaseCargo(6);
        Assert.Equal(TargetOccupancy.None, claims.OccupancyAt(6, 0));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void VacatedRollerRuleChecksTheTargetSurfaceNotTheTilesOwnSlot(bool deckOccupied)
    {
        // Tile 1: the roller's top in the tile's own slot and a deck at 2.5 in an overflow slot.
        var (grid, inputs, compiler) = NavTest.Create(3, 1, new PathfindingSettings { LayeringEnabled = true });
        inputs.Publish(NavTest.Record(10, 1, [1], h: 0.5, interaction: InteractionType.Roller));
        inputs.Publish(NavTest.Record(21, 2, [1], z: 2.5));
        compiler.ApplyNow();
        var deck = grid.SlotOf(new SurfaceRef(1, 21, SurfaceKind.Top));
        Assert.Equal((1, true), (grid.SurfaceAt(1, 0), deck >= grid.TileCount));
        var occupancy = new PlanningOccupancy(grid.SlotCapacity);
        occupancy.Targets[deckOccupied ? deck : 1] = TargetOccupancy.Stationary;
        var result = new MovementRules(grid, new()).CanRollOntoVacatedRoller(new(), new(0, 0, 3), grid.Position(deck), occupancy);
        Assert.Equal(deckOccupied ? StepReason.Occupied : StepReason.Ok, result.Reason);
    }

    private static PrefixCandidate[] Find(NavGrid grid, PlanningOccupancy occupancy, RetainedStep[] steps)
    {
        var settings = new PathfindingSettings { LayeringEnabled = true };
        var graph = new NavPrefixGraph(grid, new MovementRules(grid, settings), new ActorProfile(), occupancy);
        return new ValidPrefixFinder().Find(new PrefixCandidate(0, 0, 2, 10, 0), steps, graph);
    }

    private static RoomUser Actor() => (RoomUser)RuntimeHelpers.GetUninitializedObject(typeof(RoomUser));
}
