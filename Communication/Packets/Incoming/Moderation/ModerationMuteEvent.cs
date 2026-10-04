using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationMute)]
internal sealed class ModerationMuteEvent(IModeratorActionService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        packet.ReadString();
        var minutes = packet.ReadInt();
        packet.ReadString();
        packet.ReadString();
        moderation.Mute(session, userId, minutes);
        return Task.CompletedTask;
    }
}
