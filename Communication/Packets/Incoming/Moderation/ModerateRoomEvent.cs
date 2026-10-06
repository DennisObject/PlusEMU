using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal sealed class ModerateRoomEvent(IModeratorActionService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadUInt();
        var locked = packet.ReadInt() == 1;
        var renamed = packet.ReadInt() == 1;
        var kickAll = packet.ReadInt() == 1;
        moderation.ModerateRoom(session, new(roomId, locked, renamed, kickAll));

        return Task.CompletedTask;
    }
}
