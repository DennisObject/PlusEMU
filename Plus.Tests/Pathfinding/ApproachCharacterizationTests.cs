using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

// Characterizes today's distant/near click behaviour of vending machines, teleporters and hoppers
// so the approach-intent work can prove it changes nothing when automatic interaction is off.
public partial class PlacedFurniRoomTests
{
    private const int DrinkId = 5;

    private Item ApproachFixture(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);

        if (kind == InteractionType.VendingMachine) {
            item.Definition.VendingIds.Add(DrinkId);
        }

        return item;
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void DistantClickOnlyWalksToTheApproachTileAndNeverStartsTheInteraction(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ApproachActor(3, 0, autoInteract: false);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.Contains("/mv 2,0,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);

        for (var i = 0; i < 4; i++) {
            ExecutorTick();
        }

        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(0, item.InteractingUser);
        Assert.Equal("0", item.LegacyDataString);
        Assert.True(actor.CanWalk);
        Assert.Equal(0, actor.CarryItemId);
        Assert.Equal(MoveOrigin.User, actor.Movement.Origin);
        Assert.Null(actor.Movement.Profile.Interaction);
    }

    [Fact]
    public void NearVendingClickLocksTheUserThenDispensesOnTheSecondItemCycle()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ExecutorActor(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser);
        Assert.False(actor.CanWalk);
        Assert.Equal("1", item.LegacyDataString);
        Assert.Equal(2, item.UpdateCounter);
        ExecutorTick();
        Assert.Equal("1", item.LegacyDataString);
        Assert.Equal(1, item.UpdateCounter);
        Assert.False(actor.CanWalk);
        Assert.Equal(0, actor.CarryItemId);
        ExecutorTick();
        Assert.Equal("0", item.LegacyDataString);
        Assert.Equal(0, item.InteractingUser);
        Assert.True(actor.CanWalk);
        Assert.False(actor.AllowOverride);
        Assert.Equal(DrinkId, actor.CarryItemId);
    }

    [Fact]
    public void NearVendingClickFacesTheMachineAndClearsMovement()
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        var actor = ExecutorActor(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.Equal(Rotation.Calculate(1, 0, 1, 1), actor.RotBody);
        Assert.False(actor.HasStatus("mv"));
        Assert.False(actor.Movement.HasIntent);
        Assert.Equal((1, 0), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BusyVendingMachineIgnoresNearAndDistantClicks(bool near)
    {
        var item = ApproachFixture(InteractionType.VendingMachine);
        item.LegacyDataString = "1";
        item.InteractingUser = 99;
        var actor = ExecutorActor(near ? 1 : 3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal(99, item.InteractingUser);
        Assert.Equal("1", item.LegacyDataString);
        Assert.True(actor.CanWalk);
        Assert.Equal((near ? 1 : 3, 0), (actor.X, actor.Y));
        Assert.False(actor.Movement.HasIntent);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void NearClickStartsTheTimedEntryWithATwoTickDelayBeforeAnyMovement(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ExecutorActor(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser);
        Assert.Equal(2, actor.TeleDelay);
        Assert.True(actor.CanWalk);
        Assert.False(actor.HasStatus("mv"));
        Assert.Equal((1, 0), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void BusyPortalRejectsANearClickWithoutTouchingTheOccupantOrTheCaller(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ExecutorActor(1, 0);
        item.InteractingUser = 99;
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(99, item.InteractingUser);
        Assert.NotEqual(2, actor.TeleDelay);
        Assert.True(actor.CanWalk);
        Assert.False(actor.Movement.Commands.Read() is { Origin: MoveOrigin.User });
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void BusyPortalStillWalksAFarClickBecauseBusynessIsOnlyCheckedWhenNear(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = ExecutorActor(2, 0);
        item.InteractingUser = 99;
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(99, item.InteractingUser);
        ExecutorTick();
        Assert.Contains("/mv 1,0,0/", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void StandingOnTheTeleporterTileCountsAsNearAndStartsTheEntry()
    {
        var item = ApproachFixture(InteractionType.Teleport);
        Assert.Equal((1, 1), (item.GetX, item.GetY));
        var actor = ExecutorActor(2, 0);
        actor.InitializePosition(1, 1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser);
        Assert.Equal(2, actor.TeleDelay);
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void LegacyEngineDistantClickOnlySetsTheWalkGoal(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = Viewer(3, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal((1, 0), (actor.GoalX, actor.GoalY));
        Assert.True(actor.PathRecalcNeeded);
        Assert.Equal(0, item.InteractingUser);
        Assert.Equal("0", item.LegacyDataString);
        Assert.True(actor.CanWalk);
        Assert.Equal((3, 0), (actor.X, actor.Y));
    }

    [Theory]
    [InlineData(InteractionType.VendingMachine)]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void LegacyEngineNearClickStartsTheInteractionWithoutSettingAWalkGoal(InteractionType kind)
    {
        var item = ApproachFixture(kind);
        var actor = Viewer(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser);
        Assert.False(actor.PathRecalcNeeded);
    }
}
