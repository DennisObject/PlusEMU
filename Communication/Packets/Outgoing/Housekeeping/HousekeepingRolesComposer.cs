using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Housekeeping;

public sealed class HousekeepingRolesComposer(int requestId, AccessAdminSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.HousekeepingRolesComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(requestId);
        packet.WriteInteger(snapshot.Revision);
        packet.WriteInteger(snapshot.ActorWeight);
        packet.WriteInteger(snapshot.Roles.Count);
        foreach (var role in snapshot.Roles)
        {
            packet.WriteInteger(role.Id);
            packet.WriteString(role.Slug);
            packet.WriteString(role.Name);
            packet.WriteString(role.Description);
            packet.WriteInteger(role.Weight);
            packet.WriteInteger(role.SecurityLevel);
            packet.WriteString(role.BadgeCode);
            packet.WriteBoolean(role.IsStaff);
            packet.WriteBoolean(role.IsHidden);
            packet.WriteInteger(role.MemberCount);
            packet.WriteInteger(role.Permissions.Length);
            foreach (var key in role.Permissions) packet.WriteString(key);
            packet.WriteInteger(role.Limits.Count);
            foreach (var (key, value) in role.Limits) { packet.WriteString(key); packet.WriteInteger(value); }
        }
        packet.WriteInteger(snapshot.Permissions.Count);
        foreach (var definition in snapshot.Permissions)
        {
            packet.WriteString(definition.Key);
            packet.WriteString(definition.Category);
            packet.WriteString(definition.Description);
            packet.WriteBoolean(definition.IsOrphan);
            packet.WriteBoolean(definition.CanGrant);
        }
        packet.WriteInteger(snapshot.Limits.Count);
        foreach (var (key, value) in snapshot.Limits) { packet.WriteString(key); packet.WriteInteger(value); }
    }
}
