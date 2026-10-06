using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableTimeTests
{
    [Fact]
    public void CalendarUsesExplicitRoomTimezoneAndElapsedUnitsUseUtc()
    {
        var time = new WiredVariableTimeUtilities(WiredVariableTimeUtilities.ValidMask, 0);
        var value = new WiredVariableValue(1609459200, null, null); // 2021-01-01 00:00 UTC, ISO week 53.
        var zone = TimeZoneInfo.CreateCustomTimeZone("probe", TimeSpan.FromHours(-5), "probe", "probe");
        Assert.Equal(19, time.Read(value, 4, zone));
        Assert.Equal(31, time.Read(value, 6, zone));
        Assert.Equal(2020, time.Read(value, 10, zone));
        Assert.Equal(53, time.Read(value, 8, zone));
        Assert.Equal(612, time.Read(value, 26, zone));
        Assert.Equal(1609459200, time.Read(value, 21, zone));
        Assert.Equal(int.MaxValue, time.Read(value, 20, zone));
        Assert.Equal(0, time.Read(value with
        {
            Value = -1
        }, 21, zone));
    }
    [Fact]
    public void TimestampModesRequirePresentTimestampsAndRespectSelectedFields()
    {
        var creation = new WiredVariableTimeUtilities(1 << 21, 1);
        var updated = creation with
        {
            Mode = 2
        };
        var value = new WiredVariableValue(99, DateTimeOffset.FromUnixTimeMilliseconds(12001), DateTimeOffset.FromUnixTimeMilliseconds(24001));
        Assert.Equal(12, creation.Read(value, 21, TimeZoneInfo.Utc));
        Assert.Equal(24, updated.Read(value, 21, TimeZoneInfo.Utc));
        Assert.Null(creation.Read(value with
        {
            CreatedAt = null
        }, 21, TimeZoneInfo.Utc));
        Assert.Null(creation.Read(value, 10, TimeZoneInfo.Utc));
        Assert.Null(WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 6250000, 0, false));
        Assert.Equal(700000161u, WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 10, 0, false));
        Assert.Equal(1500000341u, WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 10, 21, true));
    }
}
