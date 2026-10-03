using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void TimedInteractorEntryUsesScopedMovementWhileWalkingIsLocked(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);
        var actor = ExecutorActor(1, 0);
        var ownerUpdates = 0;
        _client.BeforeCapture = header =>
        { if (header == ServerPacketHeader.ObjectUpdateComposer) { Assert.True(RoomOwnerScope.IsOwner(_room)); ownerUpdates++; } };
        item.Interactor.OnTrigger(_client, item, 0, true);
        Assert.Equal(7, item.InteractingUser); Assert.Equal(2, actor.TeleDelay);
        ExecutorTick();
        Assert.Equal((1, 0), (actor.X, actor.Y));
        Assert.False(actor.CanWalk); Assert.False(actor.AllowOverride);
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        Assert.Equal(new InteractionAuthorization(1, 0, 1, 1), actor.Movement.Profile.Interaction);
        Assert.False(actor.Movement.Profile.LegacyOverride);
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        Assert.Equal("1", item.LegacyDataString); Assert.True(ownerUpdates > 0);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.CanWalk); Assert.False(actor.AllowOverride);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void TimedInteractorSecondaryUserExitsWithScopedMovement(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);
        var actor = ExecutorActor(1, 1); actor.CanWalk = false;
        item.InteractingUser2 = 7;
        ExecutorTick();
        Assert.Equal(0, item.InteractingUser2);
        Assert.True(actor.CanWalk); Assert.False(actor.AllowOverride);
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        Assert.Equal(new InteractionAuthorization(1, 1, 1, 0), actor.Movement.Profile.Interaction);
        Assert.Contains("/mv 1,0,0/", ExecutorUpdate(actor).Status);
        Assert.Equal("1", item.LegacyDataString);
        ExecutorTick();
        Assert.Equal((1, 0), (actor.X, actor.Y));
        Assert.True(actor.CanWalk); Assert.False(actor.AllowOverride);
    }

    [Fact]
    public void OneWayGateKeepsFourTickEntryDelayThenExitsAndUnlocks()
    {
        var gate = InteractionItem(10, 1, 1, InteractionType.OneWayGate);
        var actor = ExecutorActor(1, 0);
        gate.Interactor.OnTrigger(_client, gate, 0, true);
        Assert.False(actor.CanWalk); Assert.Equal(4, gate.UpdateCounter);
        Assert.False(actor.AllowOverride);
        ExecutorTick();
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        Assert.Equal(3, gate.UpdateCounter);
        ExecutorTick(); Assert.Equal((1, 1), (actor.X, actor.Y)); Assert.Equal(2, gate.UpdateCounter);
        ExecutorTick(); Assert.Equal((1, 1), (actor.X, actor.Y)); Assert.Equal(1, gate.UpdateCounter);
        Assert.False(actor.HasStatus("mv")); Assert.False(actor.CanWalk);
        ExecutorTick();
        Assert.Equal("1", gate.LegacyDataString);
        Assert.Contains("/mv 1,2,0/", ExecutorUpdate(actor).Status);
        Assert.Equal(new InteractionAuthorization(1, 1, 1, 2), actor.Movement.Profile.Interaction);
        ExecutorTick();
        Assert.Equal((1, 2), (actor.X, actor.Y)); Assert.False(actor.CanWalk);
        ExecutorTick();
        Assert.True(actor.CanWalk); Assert.False(actor.AllowOverride);
        Assert.Equal("0", gate.LegacyDataString); Assert.Equal(0, gate.InteractingUser);
        Assert.False(actor.InteractingGate); Assert.Equal(0u, actor.GateId);
    }

    [Fact]
    public void TimedTeleporterTransfersToLinkedItemThenUsesItsFrontExit()
    {
        var source = InteractionItem(10, 1, 1, InteractionType.Teleport);
        var target = InteractionItem(11, 2, 2, InteractionType.Teleport);
        var actor = ExecutorActor(1, 0);
        _databaseField.SetValue(null, LandingDatabase(target.Id, RoomId));
        source.Interactor.OnTrigger(_client, source, 0, true);
        ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y)); Assert.False(actor.CanWalk);
        ExecutorTick();
        Assert.Equal((2, 2), (actor.X, actor.Y));
        Assert.Equal(0, source.InteractingUser); Assert.Equal(0, target.InteractingUser2);
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        Assert.Equal(new InteractionAuthorization(2, 2, 2, 1), actor.Movement.Profile.Interaction);
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        Assert.True(actor.CanWalk); Assert.False(actor.AllowOverride);
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
        Assert.False(actor.AllowOverride);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    [InlineData(InteractionType.OneWayGate)]
    public void InteractorFarClickApproachesWithoutAutomaticallyStartingInteraction(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);
        var actor = ExecutorActor(2, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.Equal(MoveOrigin.User, actor.Movement.Origin);
        Assert.Null(actor.Movement.Profile.Interaction);
        Assert.Contains("/mv 1,0,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal(item.SquareInFront, actor.Coordinate);
        Assert.Equal(0, item.InteractingUser);
        Assert.True(actor.CanWalk); Assert.False(actor.AllowOverride);
        Assert.Equal("0", item.LegacyDataString);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    [InlineData(InteractionType.OneWayGate)]
    public void FrozenRejectsNewTimedInteractorMovementWithoutGrantingGlobalOverride(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);
        var actor = ExecutorActor(1, 0); actor.Frozen = true;
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick(); ExecutorTick();
        Assert.Equal((1, 0), (actor.X, actor.Y));
        Assert.False(actor.HasStatus("mv")); Assert.False(actor.AllowOverride);
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Commands.Read()!.Origin);
        Assert.False(actor.Movement.HasIntent);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    public void FreezedAcceptsTimedInteractionAndResumesItsScopedEntry(InteractionType kind)
    {
        var item = InteractionItem(10, 1, 1, kind);
        var actor = ExecutorActor(1, 0); actor.Freezed = true;
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        Assert.Equal(MoveOrigin.Interaction, actor.Movement.Origin);
        Assert.True(actor.Movement.HasIntent);
        Assert.False(actor.HasStatus("mv")); Assert.False(actor.AllowOverride);
        Assert.Equal((1, 0), (actor.X, actor.Y));
        actor.Freezed = false;
        ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.AllowOverride);
    }

    [Fact]
    public void TimedInteractorAuthorizationCannotEnterAnUnrelatedBlockedNeighbour()
    {
        var item = InteractionItem(10, 1, 1, InteractionType.Teleport);
        Add(12, 2, 0, height: 4, stackable: false);
        var actor = ExecutorActor(1, 0);
        item.Interactor.OnTrigger(_client, item, 0, true);
        ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        var result = new MovementRules(navigation.Grid, navigation.Settings).CanStep(actor.Movement.Profile,
            new(actor.X, actor.Y, actor.Z), navigation.Grid.Position(navigation.Grid.Tile(2, 0)),
            StepPurpose.Interaction, OccupancyView.Execution);
        Assert.False(actor.AllowOverride); Assert.False(actor.Movement.Profile.LegacyOverride);
        Assert.Equal(StepReason.InteractionDenied, result.Reason);
    }

    private Item InteractionItem(uint id, int x, int y, InteractionType kind)
    {
        var item = Add(id, x, y, type: kind);
        InitializeNativeState(item);
        if (kind is InteractionType.Teleport or InteractionType.Hopper) item.RequestUpdate(1, true);
        return item;
    }
}
