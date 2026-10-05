using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingRolesAuditComposer(int requestId, AccessAuditPage page) : IServerPacket
{
    private readonly ImmutableArray<AuditRow> _entries = page.Entries.Select(row => new AuditRow(row.Id,
        row.ActorName, row.Action, row.TargetType, row.TargetId, row.TargetName, row.Payload, row.CreatedAt)).ToImmutableArray();
    public uint MessageId => ServerPacketHeader.HousekeepingRolesAuditComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(requestId);
        packet.WriteInteger(page.Offset);
        packet.WriteInteger(page.Total);
        packet.WriteInteger(_entries.Length);
        foreach (var row in _entries)
        {
            packet.WriteInteger(row.Id);
            packet.WriteString(row.ActorName);
            packet.WriteString(row.Action);
            packet.WriteString(row.TargetType);
            packet.WriteInteger(row.TargetId);
            packet.WriteString(row.TargetName);
            packet.WriteString(row.Payload);
            packet.WriteInteger((int)Math.Clamp(row.CreatedAt?.ToUnixTimeSeconds() ?? 0, 0, int.MaxValue));
        }
    }

    private sealed record AuditRow(int Id, string ActorName, string Action, string TargetType, int TargetId,
        string TargetName, string Payload, DateTimeOffset? CreatedAt);
}
