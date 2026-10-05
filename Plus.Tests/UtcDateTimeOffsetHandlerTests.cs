using Xunit;
using Plus.Database;
using MySqlConnector;

namespace Plus.Tests;

public sealed class UtcDateTimeOffsetHandlerTests
{
    [Fact]
    public void MapsConnectorDatetimeWithMicrosecondsAsUtc()
    {
        var value = new MySqlDateTime(new DateTime(2042, 3, 4, 5, 6, 7).AddTicks(1234560));

        var mapped = new UtcDateTimeOffsetHandler().Parse(value);

        Assert.Equal(new DateTimeOffset(2042, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(1234560), mapped);
    }

    [Fact]
    public void TreatsDatabaseDatetimeAsUtc()
    {
        var value = new DateTime(2042, 3, 4, 5, 6, 7, DateTimeKind.Unspecified);

        var mapped = new UtcDateTimeOffsetHandler().Parse(value);

        Assert.Equal(TimeSpan.Zero, mapped.Offset);
        Assert.Equal(2042, mapped.Year);
        Assert.Equal(7, mapped.Second);
    }
}
