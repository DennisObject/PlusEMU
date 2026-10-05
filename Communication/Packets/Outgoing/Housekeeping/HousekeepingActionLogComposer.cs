using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public class HousekeepingActionLogComposer : IServerPacket
{
    private readonly IReadOnlyList<HousekeepingAuditEntry> _entries;
    public uint MessageId => ServerPacketHeader.HousekeepingActionLogComposer;

    public HousekeepingActionLogComposer(IReadOnlyList<HousekeepingAuditEntry> entries) => _entries = entries;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_entries.Count);
        foreach (var entry in _entries)
        {
            packet.WriteInteger(entry.Id);
            packet.WriteInteger(entry.LegacyTimestamp);
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
}
