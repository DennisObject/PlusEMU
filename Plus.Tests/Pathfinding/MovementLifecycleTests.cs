using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void CommandIntakeStaffTeleportPreservesOnlyTheDestinationWiredLandingHook()
    {
        ExecutorFloor(11, 0, 1);
        var target = ExecutorFloor(10, 3, 2, z: .75);
        var actor = ExecutorActor(0, 1);
        var events = ExecutorWalkEvents(); events.Clear();
        actor.TeleportEnabled = true; actor.MoveTo(3, 2); ExecutorTick();
        Assert.Equal(new[] { (Plus.HabboHotel.Items.Wired.WiredBoxType.TriggerWalkOnFurni, target.Id) },
            events.Select(entry => (entry.Kind, entry.Item)).ToArray());
        Assert.Equal((3, 2, .75), (actor.X, actor.Y, actor.Z));
    }

    [Fact]
    public void RoomNavigationRetriesEveryDirtyTileAfterARejectedPublication()
    {
        var navigation = new RoomNavigation(_room, _room.GetGameMap().StaticModel,
            new() { Engine = PathfindingEngine.V2 });
        navigation.Compiler.BeforePublish = _ => throw new InvalidOperationException("publication rejected");
        Assert.Throws<InvalidOperationException>((Action)navigation.ApplyDirty);
        var publishedTiles = 0;
        navigation.Compiler.BeforePublish = tiles => publishedTiles = tiles.Count;
        navigation.ApplyDirty();
        Assert.Equal(navigation.Grid.SlotCapacity, publishedTiles);
        Assert.Equal(1, navigation.Grid.Version);
    }

    [Theory]
    [InlineData(PathfindingEngine.Shadow)]
    [InlineData(PathfindingEngine.V2)]
    public void RoomNavigationOnlyIsolatesShadowCompilerFailures(PathfindingEngine engine)
    {
        var navigation = new RoomNavigation(_room, _room.GetGameMap().StaticModel, new() { Engine = engine });
        var failure = new InvalidOperationException("publication rejected");
        navigation.Compiler.BeforePublish = _ => throw failure;
        if (engine == PathfindingEngine.V2)
            Assert.Same(failure, Assert.Throws<InvalidOperationException>((Action)navigation.ApplyDirty));
        else navigation.ApplyDirty();
    }

    [Fact]
    public void CommandIntakeStaffTeleportModeUsesRoomOwnedForcedPlacement()
    {
        ExecutorFloor(10, 3, 2, z: .75);
        var actor = ExecutorActor(0, 1); actor.TeleportEnabled = true;
        var revision = actor.Movement.LocationRevision;
        actor.MoveTo(3, 2);
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        ExecutorTick();
        Assert.Equal((3, 2, .75), (actor.X, actor.Y, actor.Z));
        Assert.True(actor.Movement.LocationRevision > revision);
        Assert.False(actor.HasStatus("mv")); Assert.False(actor.Movement.HasIntent);
        Assert.Contains(actor, _room.GetGameMap().GetRoomUsers(new(3, 2)));
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.SlideObjectBundleComposer);
    }

    [Fact]
    public void BlockedStepPolicyReplansStaticBlockersWithoutWaiting()
    {
        var actor = ExecutorActor(0, 1); actor.MoveTo(3, 1); ExecutorTick();
        var blocker = ExecutorAdditionalBot(2, 1, 2); ExecutorTick();
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.False(blocker.Movement.HasIntent);
        Assert.Equal(1, actor.Movement.BlockReplans);
        Assert.False(actor.HasStatus("mv"));
        ExecutorTick();
        Assert.True(actor.HasStatus("mv"));
        Assert.NotEqual("2,1,0", actor.Statusses["mv"]);
    }

    [Fact]
    public void ForcePlacementServiceOffGraphPlacementClearsThePreviousSupportPosture()
    {
        Add(10, 1, 1, z: .25, height: .5, seat: true);
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1); ExecutorTick(); ExecutorTick();
        Assert.True(actor.HasStatus("sit"));
        var events = ExecutorWalkEvents(); events.Clear();
        actor.SetPos(2, 2, 5.1234); ExecutorTick();
        Assert.Equal((2, 2, 5.1234), (actor.X, actor.Y, actor.Z));
        Assert.Null(actor.Movement.CurrentRef);
        Assert.False(actor.HasStatus("sit")); Assert.False(actor.HasStatus("lay"));
        Assert.False(actor.IsSitting); Assert.False(actor.IsLying);
        Assert.DoesNotContain("/sit ", ExecutorUpdate(actor).Status);
        Assert.DoesNotContain("/lay ", ExecutorUpdate(actor).Status);
        Assert.Empty(events);
    }

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
