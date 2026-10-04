using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class RoomModerationPolicyTests
{
    [Fact]
    public void OrdinaryRoomOwnersCanModerateEqualWeightOrdinaryVisitors()
    {
        Assert.True(RoomModerationPolicy.CanTarget(EditorTestSupport.Access([]), EditorTestSupport.Access([])));
    }

    [Theory]
    [InlineData("room.owner.any")]
    [InlineData("moderation.tool")]
    public void StaffProtectionRequiresStrictHierarchyEvenWithoutTheOtherStaffPermission(string permission)
    {
        var target = EditorTestSupport.Access([permission], 50);
        Assert.False(RoomModerationPolicy.CanTarget(EditorTestSupport.Access([], 0), target));
        Assert.False(RoomModerationPolicy.CanTarget(EditorTestSupport.Access([PermissionKeys.RoomOwnerAny], 50), target));
        Assert.True(RoomModerationPolicy.CanTarget(EditorTestSupport.Access([PermissionKeys.RoomOwnerAny], 90), target));
    }

    [Fact]
    public void AnyRoomOwnerOverrideCannotModerateEqualOrHigherWeightOrdinaryUsers()
    {
        var actor = EditorTestSupport.Access([PermissionKeys.RoomOwnerAny], 50);
        Assert.True(RoomModerationPolicy.CanTarget(actor, EditorTestSupport.Access([], 10)));
        Assert.False(RoomModerationPolicy.CanTarget(actor, EditorTestSupport.Access([], 50)));
        Assert.False(RoomModerationPolicy.CanTarget(actor, EditorTestSupport.Access([], 90)));
    }
}
