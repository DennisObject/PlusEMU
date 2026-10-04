using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingRoleMembersComposer(int requestId, AccessMemberPage page) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HousekeepingRoleMembersComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(requestId);
        packet.WriteInteger(page.RoleId);
        packet.WriteInteger(page.Offset);
        packet.WriteInteger(page.Total);
        packet.WriteInteger(page.Members.Count);
        foreach (var member in page.Members)
        {
            packet.WriteInteger(member.Id);
            packet.WriteString(member.Username);
            packet.WriteInteger(member.ExpiresAt.HasValue ? (int)new DateTimeOffset(DateTime.SpecifyKind(member.ExpiresAt.Value, DateTimeKind.Utc)).ToUnixTimeSeconds() : 0);
        }
    }
}
