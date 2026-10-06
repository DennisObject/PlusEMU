using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Outgoing.Handshake;

public sealed class UserRightsComposer(UserRightsSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.UserRightsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(snapshot.ClubLevel);
        packet.WriteInteger(snapshot.SecurityLevel);
        packet.WriteBoolean(snapshot.Ambassador);
        packet.WriteInteger(snapshot.PrimaryRoleId);
        packet.WriteString(snapshot.PrimaryRoleName);
        packet.WriteString(snapshot.PrimaryRoleBadge);
        packet.WriteInteger(snapshot.Keys.Length);

        foreach (var key in snapshot.Keys) {
            packet.WriteString(key);
            packet.WriteInteger(1);
        }
    }
}
