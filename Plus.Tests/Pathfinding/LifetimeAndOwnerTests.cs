using System.Collections.Concurrent;
using System.Reflection;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void AdmissionServiceOldLifetimeRemovalCannotEraseAReplacementWithTheSameVirtualId()
    {
        var old = ExecutorActor(0, 1);
        var navigation = _room.GetGameMap().Navigation!;
        navigation.Remove(old);
        var client = new TestClient();
        client.SetHabbo(new Plus.HabboHotel.Users.Habbo { Id = 8, CurrentRoom = _room,
            Access = Plus.HabboHotel.Permissions.UserAccess.Empty });
        var replacement = new RoomUser(8, RoomId, old.VirtualId, _room, client, TestChatEmotions.Unused, TestRewardProgress.Unused) { X = 0, Y = 1, Z = 0 };
        var users = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager)
            .GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(_room.GetRoomUserManager())!;
        users[old.VirtualId] = replacement;
        using (RoomOwnerScope.Enter(_room)) navigation.Admit(replacement);
        Assert.Contains(replacement, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        ExecutorTick();
        Assert.Equal(NavState.Removing, old.Movement.State);
        Assert.Equal(NavState.Active, replacement.Movement.State);
        Assert.Contains(replacement, _room.GetGameMap().GetRoomUsers(new(0, 1)));
        Assert.DoesNotContain(old, _room.GetGameMap().GetRoomUsers(new(0, 1)));
    }

    [Fact]
    public void ForcePlacementServiceDoesNotTreatAnInheritedTaskAsTheRoomOwner()
    {
        var actor = ExecutorActor(0, 1);
        using (RoomOwnerScope.Enter(_room))
            Task.Run(() => actor.SetPos(2, 2, 1.25)).GetAwaiter().GetResult();
        Assert.Equal((0, 1, 0d), (actor.X, actor.Y, actor.Z));
        ExecutorTick();
        Assert.Equal((2, 2, 1.25), (actor.X, actor.Y, actor.Z));
        Assert.Null(RoomOwnerScope.CurrentOwner);
    }
}
