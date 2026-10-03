using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// §14.9 simultaneous-group contract: preflighted furniture, collective reservations, one publication,
// structural locks and stale-snapshot rejection.
public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void RejectedWalkMagicCargoLeavesItsWholeChainUnchanged(PathfindingEngine engine)
    {
        PlannerRoller(10, 1, 0, 6); PlannerRoller(11, 2, 0, 6);
        var magic = Add(20, 1, 0, z: .5, height: 1, type: InteractionType.WalkMagicTile);
        InstallRollerChainEngine(engine);
        var actor = PlannerActor(1, 2, 0, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((1, 0, .5), (magic.GetX, magic.GetY, magic.GetZ));
        Assert.Equal((2, 0, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal(0, PlannerSlides());
        if (engine == PathfindingEngine.V2) Assert.Equal(TargetOccupancy.None, PlannerOccupancy(1, 0));
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void WalkMagicCargoGroupIsPublishedOnlyAfterEveryMemberCommitted(PathfindingEngine engine)
    {
        PlannerRoller(10, 0, 1, 2); PlannerRoller(11, 1, 1, 2);
        var magic = Add(20, 1, 1, z: .5, type: InteractionType.WalkMagicTile);
        InstallRollerChainEngine(engine);
        var actor = PlannerActor(1, 0, 1, .5);
        StartPlannerRollers();
        var published = new List<string>();
        if (engine == PathfindingEngine.V2)
        {
            var compiler = _room.GetGameMap().Navigation!.Compiler; var inner = compiler.BeforePublish;
            compiler.BeforePublish = tiles => { published.Add($"{actor.X},{magic.GetX}"); inner?.Invoke(tiles); };
        }
        ExecutorTick();
        Assert.Equal((1, 1, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal((2, 1, 0d), (magic.GetX, magic.GetY, magic.GetZ));
        Assert.Equal(2, PlannerSlides());
        if (engine == PathfindingEngine.V2) Assert.Equal(new[] { "1,2" }, published);
    }

    [Fact]
    public void ExternalClaimOnACargoDestinationHoldsTheCargo()
    {
        PlannerRoller(10, 0, 1, 2);
        var cargo = PlannerCargo(20, 0, 1);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var observer = PlannerActor(1, 3, 3, 0);
        StartPlannerRollers();
        var navigation = _room.GetGameMap().Navigation!;
        Assert.True(navigation.Executor.Claims.TryClaim(observer, navigation.Grid.Tile(1, 1), ClaimKind.Exclusive, TargetOccupancy.None));
        ExecutorTick();
        Assert.Equal((0, 1, .5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal(0, PlannerSlides());
    }

    [Fact]
    public void AnnouncedWalkerKeepsItsStepAndTheCargoWaits()
    {
        PlannerRoller(10, 0, 1, 2);
        var cargo = PlannerCargo(20, 0, 1);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var walker = PlannerActor(1, 2, 1, 0);
        ExecutorTick();
        walker.MoveTo(1, 1); ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(walker).Status);
        EnableExecutorRollers(); ExecutorTick();
        Assert.Equal((0, 1, .5), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((1, 1, 0d), (walker.X, walker.Y, walker.Z));
        Assert.Equal(0, PlannerSlides());
    }

    [Fact]
    public void CargoDestinationHoldsARollerClaimThroughTheUserPhase()
    {
        PlannerRoller(10, 0, 1, 2);
        var cargo = ExecutorFloor(20, 0, 1, z: .5, height: .25);
        InstallRollerChainEngine(PathfindingEngine.V2);
        var walker = PlannerActor(1, 2, 1, 0);
        StartPlannerRollers();
        walker.MoveTo(1, 1); ExecutorTick();
        Assert.Equal((1, 1, 0d), (cargo.GetX, cargo.GetY, cargo.GetZ));
        Assert.Equal((2, 1, 0d), (walker.X, walker.Y, walker.Z));
        Assert.DoesNotContain("/mv 1,1", ExecutorUpdate(walker).Status);
        Assert.Equal(TargetOccupancy.None, PlannerOccupancy(1, 1));
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void TemporaryCargoFollowsADepartingUserInAChain(PathfindingEngine engine)
    {
        PrepareRollerChain(8, false);
        var temporary = PlannerTemporaryCargo(1, 1);
        InstallRollerChainEngine(engine);
        var actor = PlannerActor(1, 2, 1, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((3, 1, .5), (actor.X, actor.Y, actor.Z));
        Assert.Equal((2, 1, .5), (temporary.GetX, temporary.GetY, temporary.GetZ));
        Assert.Equal(2, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void TemporaryCargoRotatesWithAFullLoop(PathfindingEngine engine)
    {
        PlannerLoopRollers(false);
        var cargo = PlannerCargo(30, 2, 1); var temporary = PlannerTemporaryCargo(1, 2);
        InstallRollerChainEngine(engine);
        var first = PlannerActor(1, 1, 1, .5); var second = PlannerActor(2, 2, 2, .5);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal((2, 1), (first.X, first.Y));
        Assert.Equal((2, 2), (cargo.GetX, cargo.GetY));
        Assert.Equal((1, 2), (second.X, second.Y));
        Assert.Equal((1, 1), (temporary.GetX, temporary.GetY));
        Assert.Equal(4, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void LockedTileInsideALoadedChainHoldsOnlyTheUserEnteringIt(PathfindingEngine engine)
    {
        PrepareRollerChain(8, false);
        var cargo = PlannerCargo(300, 1, 1);
        InstallRollerChainEngine(engine);
        var head = PlannerActor(1, 2, 1, .5); var tail = PlannerActor(2, 0, 1, .5);
        _room.GetGameMap().SetFloorStatus(1, 1, 0);
        StartPlannerRollers();
        for (var cycle = 1; cycle <= 2; cycle++)
        {
            ExecutorTick();
            Assert.Equal((0, 1, .5), (tail.X, tail.Y, tail.Z));
            Assert.Equal((1 + cycle, 1), (cargo.GetX, cargo.GetY));
            Assert.Equal((2 + cycle, 1), (head.X, head.Y));
        }
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy)]
    [InlineData(PathfindingEngine.V2)]
    public void LockedLoopTileHoldsTheWholeLoop(PathfindingEngine engine)
    {
        var loop = PlannerFullLoop(engine, false);
        _room.GetGameMap().SetFloorStatus(2, 1, 0);
        StartPlannerRollers(); ExecutorTick();
        Assert.Equal(loop.Expected(0), loop.Positions());
        Assert.Equal(0, PlannerSlides());
    }

    [Theory]
    [InlineData(PathfindingEngine.Legacy, false)]
    [InlineData(PathfindingEngine.Legacy, true)]
    [InlineData(PathfindingEngine.V2, false)]
    [InlineData(PathfindingEngine.V2, true)]
    public void EarlierGroupHookThatChangesALaterGroupRejectsTheStaleGroup(PathfindingEngine engine, bool rotate)
    {
        PlannerRoller(10, 0, 1, 2); var later = PlannerRoller(20, 0, 3, 2);
        ExecutorFloor(40, 1, 1);
        InstallRollerChainEngine(engine);
        var first = PlannerActor(1, 0, 1, .5); var second = PlannerActor(2, 0, 3, .5);
        var fired = false;
        PlannerObserveWalkOn(() =>
        {
            if (fired) return;
            fired = true;
            if (rotate) later.SetPlacementState(later.GetX, later.GetY, later.GetZ, later.GetAffectedTiles, 0);
            else second.SetPos(0, 3, 1.5);
        });
        StartPlannerRollers(); ExecutorTick();
        Assert.True(fired);
        Assert.Equal((1, 1, 0d), (first.X, first.Y, first.Z));
        Assert.Equal((0, 3, rotate ? .5 : 1.5), (second.X, second.Y, second.Z));
        Assert.DoesNotContain(_client.Packets, packet => ExecutorIsAvatarSlide(packet, second.VirtualId));
    }

    private Item PlannerTemporaryCargo(int x, int y)
    {
        var definition = Furni(0, InteractionType.None, WiredBoxType.None).Definition;
        definition.Width = definition.Length = 1;
        var item = _room.GetRoomItemHandler().PlaceTemporaryFloorItem(definition, 7, x, y, 0, .5);
        Assert.NotNull(item);
        return item!;
    }

    private TargetOccupancy PlannerOccupancy(int x, int y)
    {
        var navigation = _room.GetGameMap().Navigation!;
        return navigation.Executor.Claims.OccupancyAt(navigation.Grid.Tile(x, y), 0);
    }
}
