using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public sealed class AccessAuditWireTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1L, 0)]
    [InlineData(0L, 0)]
    [InlineData(1_700_000_000L, 1_700_000_000)]
    [InlineData(2_147_483_647L, int.MaxValue)]
    [InlineData(2_147_483_648L, int.MaxValue)]
    public void AuditWireKeepsFieldOrderAndBoundsLegacySeconds(long? epoch, int expected)
    {
        var created = epoch.HasValue ? DateTimeOffset.FromUnixTimeSeconds(epoch.Value).AddTicks(9_999_999) : (DateTimeOffset?)null;
        var packet = new HabbiconTestSupport.RecordingPacket();
        new HousekeepingRolesAuditComposer(5, new(10, 30, [Row(created)])).Compose(packet);
        Assert.Equal(new object[] { 5, 10, 30, 1, 7, "actor", "role.assign", "user", 8, "target", "{}", expected }, packet.Writes);
    }

    [Fact]
    public void AuditComposerCopiesRowsAndListBeforeSourceMutation()
    {
        var row = Row(DateTimeOffset.FromUnixTimeSeconds(100));
        var entries = new List<AccessAuditEntry> { row };
        var composer = new HousekeepingRolesAuditComposer(5, new(10, 30, entries));
        var before = Writes(composer);
        row.ActorName = "changed";
        row.CreatedAt = DateTimeOffset.MaxValue;
        entries[0] = Row(null);
        entries.Add(Row(null));
        Assert.Equal(before, Writes(composer));
        Assert.Equal(before, Writes(composer));
    }

    private static AccessAuditEntry Row(DateTimeOffset? created) => new()
    {
        Id = 7, ActorName = "actor", Action = "role.assign", TargetType = "user", TargetId = 8,
        TargetName = "target", Payload = "{}", CreatedAt = created
    };
    private static object[] Writes(HousekeepingRolesAuditComposer composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes.ToArray();
    }
}
