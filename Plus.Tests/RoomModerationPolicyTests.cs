using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomModerationPolicyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 50)]
    public void OrdinaryRoomOwnersCanModerateOrdinaryVisitorsRegardlessOfRoleWeight(int actorWeight, int targetWeight)
    {
        Assert.True(RoomModerationPolicy.CanTarget(Access(actorWeight), Access(targetWeight)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("command.update")]
    [InlineData("room.owner.any")]
    [InlineData("moderation.tool")]
    public void AnyStaffRoleProtectsTargetEvenWithoutModerationPermissions(string permission)
    {
        var target = Access(50, staff: true, permission);
        Assert.False(RoomModerationPolicy.CanTarget(Access(0), target));
        Assert.False(RoomModerationPolicy.CanTarget(Access(50), target));
        Assert.True(RoomModerationPolicy.CanTarget(Access(90), target));
    }

    [Fact]
    public void AnyRoomOwnerOverrideCannotModerateEqualOrHigherWeightOrdinaryUsers()
    {
        var actor = Access(50, permission: PermissionKeys.RoomOwnerAny);
        Assert.True(RoomModerationPolicy.CanTarget(actor, Access(10)));
        Assert.False(RoomModerationPolicy.CanTarget(actor, Access(50)));
        Assert.False(RoomModerationPolicy.CanTarget(actor, Access(90)));
    }

    [Fact]
    public void StaffRoleProtectsTargetAlongsideHigherWeightNonStaffRole()
    {
        var target = UserAccess.Create([
            new(new AccessRole(1, "staff", "Staff", 20, 1, "", true, [], new Dictionary<string, int>())),
            new(new AccessRole(2, "vip", "VIP", 50, 1, "", false, [], new Dictionary<string, int>()))]);
        Assert.False(RoomModerationPolicy.CanTarget(Access(40), target));
        Assert.True(RoomModerationPolicy.CanTarget(Access(60), target));
    }

    private static UserAccess Access(int weight, bool staff = false, string permission = "") => UserAccess.Create([
        new(new AccessRole(1, "role", "Role", weight, 1, "", staff,
            permission.Length == 0 ? [] : [permission], new Dictionary<string, int>()))]);
}
