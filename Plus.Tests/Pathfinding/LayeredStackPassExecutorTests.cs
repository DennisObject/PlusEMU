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
}
