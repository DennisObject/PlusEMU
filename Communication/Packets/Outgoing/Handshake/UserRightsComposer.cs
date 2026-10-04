using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public sealed class UserRightsComposer(UserAccess access) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserRightsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        // Capture all fields together so an expiry cannot mix two resolutions in one packet.
        var resolved = access.Capture();
        var roles = resolved.Roles;
        var primary = roles.OrderByDescending(role => role.Weight).ThenBy(role => role.Id).FirstOrDefault();
        var keys = resolved.Keys.Order(StringComparer.Ordinal).ToArray();
        packet.WriteInteger(ClubAccess.LevelFor(resolved, access.Now));
        packet.WriteInteger(resolved.SecurityLevel);
        packet.WriteBoolean(keys.Contains(PermissionKeys.Ambassador, StringComparer.Ordinal));
        packet.WriteInteger(primary?.Id ?? 0);
        packet.WriteString(primary?.Name ?? string.Empty);
        packet.WriteString(primary?.BadgeCode ?? string.Empty);
        packet.WriteInteger(keys.Length);
        foreach (var key in keys)
        {
            packet.WriteString(key);
            packet.WriteInteger(1);
        }
    }
}
