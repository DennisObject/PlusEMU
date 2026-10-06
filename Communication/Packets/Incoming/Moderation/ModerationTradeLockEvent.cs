using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTradeLock)]
internal class ModerationTradeLockEvent(ITradeModerationService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var message = packet.ReadString();
        var minutes = packet.ReadInt();
        packet.ReadString();
        packet.ReadString();

        return moderation.Lock(session, userId, minutes, message);
    }
}
