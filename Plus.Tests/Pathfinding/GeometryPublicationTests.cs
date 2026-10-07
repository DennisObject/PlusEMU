using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void RebindSeatPickupAtUnchangedZEmitsStandingCorrectionWithoutLandingHooks()
    {
        var seat = Add(10, 1, 1, height: .5, seat: true);
        var events = ExecutorWalkEvents();
        var actor = GeometrySeatedActor();
        events.Clear();
        _room.GetRoomItemHandler().RemoveFurniture(null!, seat.Id);
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("sit"));
        Assert.False(actor.HasStatus("lay"));
        Assert.Equal(SurfaceKind.Floor, actor.Movement.CurrentRef!.Value.Kind);
        Assert.Empty(events);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.UserUpdateComposer);
        Assert.DoesNotContain("/sit ", ExecutorUpdate(actor).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebindReplacementClearsPostureAndCorrectsEvenWithoutZChange(bool blocker)
    {
        Add(10, 1, 1, height: .5, seat: true);
        var events = ExecutorWalkEvents();
        var actor = GeometrySeatedActor();
        events.Clear();

        if (blocker) {
            GeometryMoveBlockerThroughActor(actor);
        }
        else {
            Add(11, 1, 1, type: InteractionType.WalkMagicTile);
        }

        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.False(actor.HasStatus("sit"));
        Assert.False(actor.HasStatus("lay"));

        if (blocker) {
            Assert.Null(actor.Movement.CurrentRef);
        }
        else {
            Assert.Equal(SurfaceKind.WalkMagic, actor.Movement.CurrentRef!.Value.Kind);
        }

        Assert.Empty(events);
        Assert.Contains(_client.Packets, packet => packet.Header == ServerPacketHeader.UserUpdateComposer);
        Assert.DoesNotContain("/sit ", ExecutorUpdate(actor).Status);
    }

    [Theory]
    [InlineData(3, 1, true)]
    [InlineData(3, 0, true)]
    [InlineData(0, 3, false)]
    public void OwnerGeometryPublicationInvalidatesOnlyTheRemainingRouteNeighborhood(int x, int y, bool invalidates)
    {
        var landing = ExecutorFloor(10, 1, 1);
        var changed = Add(11, x, y, type: InteractionType.WalkMagicTile);
        var actor = ExecutorActor(0, 1);
        var observed = new List<long>();
        ExecutorObserveLanding((user, item) =>
        {
            if (item != landing) {
                return;
            }

            Assert.True(RoomOwnerScope.IsOwner(_room));
            Assert.True(_room.GetRoomItemHandler().SetFloorItem(changed, x, y, .5));
            observed.Add(user.Movement.GoalRevision);
        });
        actor.MoveTo(3, 1);
        ExecutorTick();
        var goalRevision = actor.Movement.GoalRevision;
        var locationRevision = actor.Movement.LocationRevision;
        ExecutorTick();
        Assert.Equal(invalidates, Assert.Single(observed) > goalRevision);
        Assert.Equal(locationRevision, actor.Movement.LocationRevision);
        Assert.Equal((1, 1), (actor.X, actor.Y));
        Assert.Same(landing, actor.LastItem);
        Assert.True(actor.Movement.HasIntent);
        Assert.Contains("/mv 2,1,0/", ExecutorUpdate(actor).Status);
    }

    [Fact]
    public void OwnerSupportRemovalReleasesAnotherActorsPendingBatchClaimsInline()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var removed = ExecutorFloor(11, 3, 2);
        var first = ExecutorActor(0, 1);
        var waiting = ExecutorAdditionalBot(2, 2, 2);
        ExecutorTick();
        var observed = new List<(int Pending, TargetOccupancy Claims, bool Intent)>();
        GeometryObserveRemoval(landing, removed, waiting, observed);
        first.MoveTo(1, 1);
        waiting.MoveTo(3, 2);
        ExecutorTick();
        Assert.Equal(1, waiting.Movement.PendingCount);
        Assert.NotEqual(TargetOccupancy.None, GeometryClaimsAt(3, 2));
        ExecutorTick();
        var publication = Assert.Single(observed);
        Assert.Equal(0, publication.Pending);
        Assert.Equal(TargetOccupancy.None, publication.Claims);
        Assert.True(publication.Intent);
        Assert.Equal((2, 2), (waiting.X, waiting.Y));
        Assert.Equal((1, 1), (first.X, first.Y));
        Assert.Same(landing, first.LastItem);
    }

    // Wired-style move: the announced support leaves a tile that stays walkable, so the step lands at the floor height.
    [Fact]
    public void OwnerSupportMoveLandsAnotherActorsPendingStepAtTheNewHeight()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var moved = ExecutorFloor(11, 3, 2, height: .5);
        var waiting = GeometryWaitingOn(landing, () => Assert.True(_room.GetRoomItemHandler().SetFloorItem(moved, 3, 3, 0)));
        Assert.Contains("/mv 3,2,0.5/", ExecutorUpdate(waiting).Status);
        ExecutorTick();
        Assert.Equal((3, 2, 0d), (waiting.X, waiting.Y, waiting.Z));
        Assert.Equal(SurfaceKind.Floor, waiting.Movement.CurrentRef!.Value.Kind);
    }

    // A tile that became unwalkable between announce and commit still releases the batch.
    [Fact]
    public void OwnerBlockerOnAPendingTargetReleasesTheBatch()
    {
        var landing = ExecutorFloor(10, 1, 1);
        var waiting = GeometryWaitingOn(landing, () => Add(12, 3, 2, height: 1, stackable: false));
        ExecutorTick();
        Assert.Equal((2, 2), (waiting.X, waiting.Y));
        Assert.Equal(0, waiting.Movement.PendingCount);
        Assert.Equal(TargetOccupancy.None, GeometryClaimsAt(3, 2));
    }

    // A bot at (2, 2) has announced (3, 2); the first actor's next landing on `landing` runs `change` and publishes it,
    // as a wired pass does between room ticks.
    private RoomUser GeometryWaitingOn(Item landing, Action change)
    {
        var first = ExecutorActor(0, 1);
        var waiting = ExecutorAdditionalBot(2, 2, 2);
        ExecutorTick();
        ExecutorObserveLanding((_, item) =>
        {
            if (item == landing) {
                change();
                _room.GetGameMap().Navigation!.ApplyDirty();
            }
        });
        first.MoveTo(1, 1);
        waiting.MoveTo(3, 2);
        ExecutorTick();
        Assert.Equal(1, waiting.Movement.PendingCount);

        return waiting;
    }

    private void GeometryMoveBlockerThroughActor(RoomUser actor)
    {
        var blocker = Add(11, 0, 2, z: 1, height: 1, stackable: false);
        var policy = new WiredCollisionPolicy(new HashSet<uint>(), new HashSet<int> { actor.VirtualId },
            new HashSet<uint>());
        Assert.True(WiredRoomOperations.MoveItem(_room, blocker, 1, 1, height: 1, collision: policy));
    }

    private RoomUser GeometrySeatedActor()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(1, 1);
        ExecutorTick();
        ExecutorTick();
        Assert.Equal((1, 1, 0d), (actor.X, actor.Y, actor.Z));
        Assert.True(actor.HasStatus("sit"));

        return actor;
    }

    private TargetOccupancy GeometryClaimsAt(int x, int y)
    {
        var navigation = _room.GetGameMap().Navigation!;
        var claims = navigation.Executor.Claims.OccupancyAt(navigation.Grid.Tile(x, y), 0);

        return claims & (TargetOccupancy.ExclusiveClaim | TargetOccupancy.GoalClaim | TargetOccupancy.SharedClaim);
    }

    private void GeometryObserveRemoval(Item landing, Item removed, RoomUser waiting,
        List<(int Pending, TargetOccupancy Claims, bool Intent)> observed)
    {
        ExecutorObserveLanding((_, item) =>
        {
            if (item != landing) {
                return;
            }

            Assert.True(RoomOwnerScope.IsOwner(_room));
            _room.GetRoomItemHandler().RemoveFurniture(null!, removed.Id);
            observed.Add((waiting.Movement.PendingCount, GeometryClaimsAt(3, 2), waiting.Movement.HasIntent));
        });
    }
}
