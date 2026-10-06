using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingActionLogComposer : IServerPacket
{
    private readonly ImmutableArray<HousekeepingAuditEntry> _entries;
    public uint MessageId => ServerPacketHeader.HousekeepingActionLogComposer;

    // Entries are copied at construction, so later changes to the caller's list do not reach the packet.
    public HousekeepingActionLogComposer(IReadOnlyList<HousekeepingAuditEntry> entries) => _entries = entries.ToImmutableArray();

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_entries.Length);
        foreach (var entry in _entries)
        {
            packet.WriteInteger(entry.Id);
            packet.WriteInteger(LegacyUnixSeconds(entry.CreatedAt));
            packet.WriteInteger(entry.ActorId);
            packet.WriteString(entry.ActorName);
            packet.WriteString(entry.TargetType);
            packet.WriteInteger(entry.TargetId);
            packet.WriteString(entry.TargetLabel);
            packet.WriteString(entry.Action);
            packet.WriteString(entry.Detail);
            packet.WriteBoolean(entry.Success);
        }
    }

    // The client still reads Unix seconds. A missing or pre-1970 time sends 0, and a time past 2038 clamps to int.MaxValue.
    private static int LegacyUnixSeconds(DateTimeOffset? createdAt) => createdAt is { } value
        ? (int)Math.Clamp(value.ToUnixTimeSeconds(), 0, int.MaxValue)
        : 0;
}
