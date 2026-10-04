using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingUserOverridesComposer(int requestId, AccessOverridePage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HousekeepingUserOverridesComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(requestId);
        packet.WriteInteger(page.UserId);
        packet.WriteString(page.Username);
        packet.WriteInteger(page.Overrides.Count);
        foreach (var row in page.Overrides)
        {
            packet.WriteString(row.Key);
            packet.WriteString(row.Effect);
            packet.WriteString(row.Reason);
            packet.WriteInteger(row.ExpiresAt ?? 0);
        }
    }
}
