using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Layered passes over roller transport (S3) and approach intents (S5) in a real room.
public partial class PlacedFurniRoomTests
{
    [Fact]
    public void LayeredRollerClaimBindsToTheSurfaceAtTheCarriedZ()
    {
        // (1,1): the deck holds the tile's own slot; the rug on the floor below returns in an overflow slot.
        ExecutorRoller(10, 0, 1);
        var blocker = ExecutorFloor(13, 1, 1, height: 1); blocker.Definition.Walkable = false;
        ExecutorFloor(12, 1, 1, z: 0); ExecutorFloor(21, 1, 1, z: 2);
        var actor = LayeredActor(0, 1);
        _room.GetRoomItemHandler().RemoveFurniture(null!, 13);
        actor.SetPos(0, 1, 0.5); ExecutorTick();
        var grid = LayeredNavigation.Grid; var claims = LayeredNavigation.Executor.Claims;
        var rug = grid.SlotOf(new SurfaceRef(LayeredTile(1, 1), 12, SurfaceKind.Top));
        Assert.Equal((LayeredTile(1, 1), true), (grid.SlotOf(new SurfaceRef(LayeredTile(1, 1), 21, SurfaceKind.Top)), rug >= grid.TileCount));
        var observed = new List<(TargetOccupancy Rug, TargetOccupancy Deck)>();
        ExecutorObserveLanding((_, item) => { if (item.Id == 12) observed.Add((claims.OccupancyAt(rug, 0), claims.OccupancyAt(LayeredTile(1, 1), 0))); });
        EnableExecutorRollers(); ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(new[] { (TargetOccupancy.Stationary | TargetOccupancy.RollerClaim, TargetOccupancy.None) }, observed);
    }

    [Fact]
    public void LayeredRollerClaimFollowsItsSurfaceWhenTheRoomLeavesLayers()
    {
        LayeredBridge();
        var actor = LayeredActor(0, 3);
        var deck = new SurfaceRef(LayeredTile(2, 1), 21, SurfaceKind.Top);
        var overflow = LayeredNavigation.Grid.SlotOf(deck);
        var claims = LayeredNavigation.Executor.Claims;
        Assert.True(claims.TryClaim(actor, overflow, ClaimKind.Roller, TargetOccupancy.None));
        ExecutorFloor(60, 3, 3).Definition.InteractionType = InteractionType.Banzaifloor;
        LayeredNavigation.Inputs.Attach(_room.GetRoomItemHandler().GetItem(60));
        using (RoomOwnerScope.Enter(_room)) LayeredNavigation.ApplyDirty();
        Assert.Equal(LayeredTile(2, 1), LayeredNavigation.Grid.SlotOf(deck));
        Assert.Equal(TargetOccupancy.None, claims.OccupancyAt(overflow, 0));
        Assert.Equal(TargetOccupancy.RollerClaim, claims.OccupancyAt(LayeredTile(2, 1), 0));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(2d)]
    public void LayeredApproachSurfaceIsTheApproachTileSurfaceAtTheItemsLevel(double vendingZ)
    {
        // Approach tile (1,0) holds a floor and a deck at 2; the actor starts on decks at (2,0)-(3,0).
        // The tile's own slot holds the surface the item is NOT on, so slot == tile would pick the wrong level.
        var vending = Add(10, 1, 1, z: vendingZ, type: InteractionType.VendingMachine);
        InitializeNativeState(vending); vending.Definition.VendingIds.Add(DrinkId);
        Item? blocker = null;
        if (vendingZ == 0) { blocker = ExecutorFloor(30, 1, 0, height: 1); blocker.Definition.Walkable = false; }
        ExecutorFloor(31, 1, 0, z: 2); ExecutorFloor(32, 2, 0, z: 2); ExecutorFloor(33, 3, 0, z: 2);
        var actor = LayeredActor(3, 0);
        if (blocker != null) _room.GetRoomItemHandler().RemoveFurniture(null!, blocker.Id);
        actor.SetPos(3, 0, 2); ExecutorTick();
        Assert.Equal(vendingZ == 0 ? SurfaceKind.Top : SurfaceKind.Floor, LayeredNavigation.Grid.Kind[LayeredTile(1, 0)]);
        vending.Interactor.OnTrigger(_client, vending, 0, true);
        for (var tick = 0; tick < 4; tick++) ExecutorTick();
        Assert.Equal((1, 0, vendingZ), (actor.X, actor.Y, actor.Z));
        Assert.Equal(7, vending.InteractingUser);
    }

    [Fact]
    public void LayeredRollerCarriesOnlyWhatRestsOnTheRollerSurface()
    {
        // (0,1): roller top 0.5 with a rug on it, and a zero-height deck at 2.5 above it.
        var roller = ExecutorRoller(10, 0, 1);
        var rug = ExecutorFloor(12, 0, 1, z: 0.5); var deck = ExecutorFloor(21, 0, 1, z: 2.5);
        var upper = LayeredActor(0, 1);
        upper.SetPos(0, 1, 2.5); ExecutorTick();
        var rider = ExecutorAdditionalBot(3, 3, 5);
        rider.SetPos(0, 1, 0.5); ExecutorTick();
        Assert.Equal((2.5, 0.5), (upper.Z, rider.Z));
        Assert.Equal(new[] { upper, rider }, _room.GetGameMap().GetRoomUsers(new(0, 1)).ToArray());
        EnableExecutorRollers(); ExecutorTick();
        Assert.Equal((0, 1, 2.5), (upper.X, upper.Y, upper.Z));
        Assert.Equal((1, 1, 0d), (rider.X, rider.Y, rider.Z));
        Assert.Equal((0, 1), (deck.GetX, deck.GetY));
        Assert.Equal((1, 1), (rug.GetX, rug.GetY));
        Assert.Equal((0, 1), (roller.GetX, roller.GetY));
    }

    [Fact]
    public void LayeredApproachKeepsItsSurfaceWhenTheGoalIsReResolved()
    {
        var vending = LayeredDeckApproach();
        var actor = LayeredDeckApproacher();
        vending.Interactor.OnTrigger(_client, vending, 0, true); ExecutorTick();
        ExecutorFloor(34, 1, 0);
        for (var tick = 0; tick < 2; tick++) ExecutorTick();
        Assert.Equal((1, 0, 2d), (actor.X, actor.Y, actor.Z));
        Assert.Equal(7, vending.InteractingUser);
    }

    [Fact]
    public void LayeredApproachIsDroppedWhenItsSurfaceIsRemoved()
    {
        var vending = LayeredDeckApproach();
        var actor = LayeredDeckApproacher();
        vending.Interactor.OnTrigger(_client, vending, 0, true); ExecutorTick();
        Assert.NotNull(Approaches.Peek(actor));
        _room.GetRoomItemHandler().RemoveFurniture(null!, 31);
        for (var tick = 0; tick < 5; tick++) ExecutorTick();
        Assert.Equal(0, vending.InteractingUser);
        Assert.Null(Approaches.Peek(actor));
    }

    // A vending machine at deck level (2) beside approach tile (1,0), which has a floor and a deck; decks lead from (3,0).
    private Item LayeredDeckApproach()
    {
        var vending = Add(10, 1, 1, z: 2, type: InteractionType.VendingMachine);
        InitializeNativeState(vending); vending.Definition.VendingIds.Add(DrinkId);
        ExecutorFloor(31, 1, 0, z: 2); ExecutorFloor(32, 2, 0, z: 2); ExecutorFloor(33, 3, 0, z: 2);
        return vending;
    }

    private RoomUser LayeredDeckApproacher()
    {
        var actor = LayeredActor(3, 0);
        actor.SetPos(3, 0, 2); ExecutorTick();
        return actor;
    }
}
