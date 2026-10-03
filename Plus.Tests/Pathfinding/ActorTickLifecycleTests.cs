using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void ExecutorDiscardsAnActorWhoseClientLeavesTheRoomWhileABatchIsPending()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        _client.GetHabbo().CurrentRoom = null!;
        ExecutorTick();
        Assert.Equal(NavState.Removing, actor.Movement.State);
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.False(actor.Movement.HasIntent); Assert.False(actor.HasStatus("mv"));
        Assert.DoesNotContain(actor, _room.GetRoomUserManager().GetUserList());
        Assert.DoesNotContain(actor, _room.GetGameMap().GetRoomUsers(new(0, 1)));
    }

    [Fact]
    public void ExecutorAutokickRemovesTheActorBeforeItsPendingLanding()
    {
        var actor = ExecutorActor(0, 1);
        actor.MoveTo(3, 1); ExecutorTick();
        actor.NeedsAutokick = true;
        ExecutorTick();
        Assert.Equal(NavState.Removing, actor.Movement.State);
        Assert.Equal((0, 1), (actor.X, actor.Y));
        Assert.Equal(0, actor.Movement.PendingCount);
        Assert.Null(_client.GetHabbo().CurrentRoom);
        Assert.DoesNotContain(actor, _room.GetRoomUserManager().GetUserList());
    }
}
