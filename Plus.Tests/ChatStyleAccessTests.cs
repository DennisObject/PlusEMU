using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Styles;
using Xunit;

namespace Plus.Tests;

public class ChatStyleAccessTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void StylesRequireEnabledStateAndMembership(bool enabled, bool requiresHc, bool allowed)
    {
        var style = new ChatStyle(1, "Style", "", requiresHc, enabled);
        Assert.Equal(allowed, style.CanUse(UserAccess.Empty));
    }

    [Fact]
    public void RestrictedStyleRequiresBothItsPermissionAndMembership()
    {
        var style = new ChatStyle(1, "Staff", PermissionKeys.ChatStyleStaff, requiresHc: true);
        Assert.False(style.CanUse(UserAccess.Empty));
        Assert.True(style.CanUse(UserAccess.Create([], [new(PermissionKeys.ChatStyleStaff, false), new(PermissionKeys.ClubAccess, false)])));
        Assert.False(style.CanUse(UserAccess.Create([], [new("*", false), new(PermissionKeys.ChatStyleStaff, true)])));
    }
}
