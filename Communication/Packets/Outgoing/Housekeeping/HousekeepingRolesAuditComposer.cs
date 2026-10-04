using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingRolesAuditComposer(int requestId, AccessAuditPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HousekeepingRolesAuditComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(requestId);
        packet.WriteInteger(page.Offset);
        packet.WriteInteger(page.Total);
        packet.WriteInteger(page.Entries.Count);
        foreach (var row in page.Entries)
        {
            packet.WriteInteger(row.Id);
            packet.WriteString(row.ActorName);
            packet.WriteString(row.Action);
            packet.WriteString(row.TargetType);
            packet.WriteInteger(row.TargetId);
            packet.WriteString(row.TargetName);
            packet.WriteString(row.Payload);
            packet.WriteInteger(row.CreatedAt);
        }
    }
}
