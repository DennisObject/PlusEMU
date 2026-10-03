using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void AdmissionServiceKeepsPendingActorsOutOfTheMapUntilOwnerDrain()
    {
        var actor = ExecutorActor(0, 1);
        var navigation = _room.GetGameMap().Navigation!;
        var newcomer = new Plus.HabboHotel.Rooms.RoomUser(0, _room.RoomId, 99, _room) { X = 2, Y = 2, Z = 0 };
        navigation.Admit(newcomer);
        Assert.Equal(NavState.PendingAdmission, newcomer.Movement.State);
        Assert.DoesNotContain(newcomer, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        ExecutorTick();
        Assert.Equal(NavState.Active, newcomer.Movement.State);
        Assert.Contains(newcomer, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.Equal(NavState.Active, actor.Movement.State);
    }

    [Fact]
    public void AdmissionServiceIgnoresCommandsForAnActorMarkedRemovingBeforeDrain()
    {
        var actor = ExecutorActor(0, 1);
        var navigation = _room.GetGameMap().Navigation!;
        navigation.ForcePlace(actor, 2, 2, 0, ForceResolution.ExactZ);
        navigation.Remove(actor);
        Assert.Equal(NavState.Removing, actor.Movement.State);
        ExecutorTick();
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(2, 2)));
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void CommandIntakeCanWalkRejectsUserButAllowsWiredOrigin()
    {
        var actor = ExecutorActor(0, 1); actor.CanWalk = false;
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.False(actor.HasStatus("mv"));
        actor.MoveTo(2, 1, MoveOrigin.Wired); ExecutorTick();
        Assert.Equal((2, 1), (actor.GoalX, actor.GoalY));
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void LegacyExistingRouteContinuesAfterCanWalkIsDisabled()
    {
        var actor = Viewer(0, 1); actor.UserId = 7;
        _room.GetGameMap().AddUserToMap(actor, new(0, 1));
        actor.MoveTo(2, 1); ExecutorTick();
        Assert.Contains("/mv 1,1,0/", ExecutorUpdate(actor).Status);
        actor.CanWalk = false; ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((2, 1), (actor.X, actor.Y));
        Assert.False(actor.HasStatus("mv"));
    }

    [Fact]
    public void ForcePlacementServiceExactZReleasesClaimsAndRejectsStaleRoomCommands()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(2, 1); ExecutorTick();
        var navigation = _room.GetGameMap().Navigation!;
        var revision = actor.Movement.LocationRevision;
        navigation.ForcePlace(actor, 2, 2, 0, ForceResolution.ExactZ); ExecutorTick();
        Assert.Equal(revision + 1, actor.Movement.LocationRevision);
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.Equal(0, actor.Movement.Route.Count);
        Assert.False(actor.Movement.HasIntent);
        actor.MoveTo(3, 2); ExecutorTick(); ExecutorTick();
        Assert.Equal((3, 2), (actor.X, actor.Y));
    }

    [Fact]
    public void RebindServiceRemovedSupportFallsBackWithoutFabricatingWalkEvents()
    {
        var floor = ExecutorFloor(10, 1, 1, z: .5, height: .5);
        var events = ExecutorWalkEvents();
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick(); events.Clear();
        _room.GetRoomItemHandler().RemoveFurniture(null!, floor.Id);
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.Empty(events);
        Assert.Equal(SurfaceKind.Floor, actor.Movement.CurrentRef!.Value.Kind);
    }

    [Theory]
    [InlineData(InteractionType.Teleport)]
    [InlineData(InteractionType.Hopper)]
    [InlineData(InteractionType.OneWayGate)]
    public void CommandIntakeInteractionAuthorizesOnlyItsAdjacentEntry(InteractionType interaction)
    {
        var entry = Add(10, 1, 1, height: 4, stackable: false, type: interaction);
        Add(11, 2, 1, height: 4, stackable: false);
        var actor = ExecutorActor(0, 1); actor.CanWalk = false;
        _room.GetGameMap().Navigation!.InteractionStep(actor, entry.GetX, entry.GetY);
        ExecutorTick();
        Assert.Contains("/mv 1,1,4/", ExecutorUpdate(actor).Status);
        ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(actor.AllowOverride);
        actor.CanWalk = true; actor.MoveTo(2, 1); ExecutorTick();
        Assert.False(actor.HasStatus("mv"));
    }
}
