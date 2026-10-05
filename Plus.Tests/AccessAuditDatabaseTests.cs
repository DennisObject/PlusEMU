using Dapper;
using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public sealed partial class AccessControlDatabaseTests
{
    [AccessControlDatabaseFact]
    public void AuditLoaderKeepsNativeUtcFractionsBeyond2038AndConvertsOnlyOnTheWire()
    {
        var created = new DateTimeOffset(2040, 1, 2, 3, 4, 5, TimeSpan.FromHours(9)).AddTicks(1_234_560);
        using var connection = _database.Connection();
        connection.Execute("""
            INSERT INTO acl_audit_log (actor_id, action, target_type, target_id, payload, created_at)
            VALUES (@Actor, 'utc.audit', 'user', @Target, '{}', @createdAt)
            """, new { Actor, Target, createdAt = created.UtcDateTime });
        var page = _access.Audit(_actor, 0);
        var row = Assert.Single(page.Entries, entry => entry.Action == "utc.audit");
        Assert.Equal(created.ToUniversalTime(), row.CreatedAt);
        var packet = new HabbiconTestSupport.RecordingPacket();
        new HousekeepingRolesAuditComposer(5, new AccessAuditPage(0, page.Total, [row])).Compose(packet);
        Assert.Equal(int.MaxValue, packet.Writes[11]);
    }
}
