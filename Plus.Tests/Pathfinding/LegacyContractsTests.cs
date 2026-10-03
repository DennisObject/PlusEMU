using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class LegacyContractsTests
{
    [Fact]
    public void RetainedStressAndStaffContractsRunOnProductionV2Search()
    {
        var rows = new[] { "xxxxx", "x000x", "x0x0x", "x000x", "xxxxx" };
        var (grid, _, _) = NavTest.Create(5, 5, states: string.Concat(rows).Select(c => c == 'x' ? SquareState.Blocked : SquareState.Open).ToArray());
        var occupancy = new PlanningOccupancy(25);
        Array.Fill(occupancy.Targets, TargetOccupancy.Stationary);
        var stress = new ActorProfile { IgnoreUsers = true };
        var staff = new ActorProfile { LegacyOverride = true };
        var ws = new PathWorkspace(25, 25); var route = new Route(); var search = new PathSearch(grid, new());
        Assert.Equal(PathOutcome.Found, search.Find(new(stress, grid.Position(11), 3, 2, occupancy), ws, route));
        Assert.DoesNotContain(route.Steps.ToArray(), p => p.Tile == 12);
        Assert.Equal(PathOutcome.Found, search.Find(new(staff, grid.Position(11), 3, 2, occupancy), ws, route));
        Assert.Contains(route.Steps.ToArray(), p => p.Tile == 12);
        var (cliff, _, _) = NavTest.Create(3, 1, z: [0, 2, 0]);
        Assert.Equal(PathOutcome.Unreachable, new PathSearch(cliff, new()).Find(new(stress, cliff.Position(0), 2, 0), ws, route));
        Assert.Equal(PathOutcome.Found, new PathSearch(cliff, new()).Find(new(staff, cliff.Position(0), 2, 0), ws, route));
    }
}
