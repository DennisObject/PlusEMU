using Plus.Communication.Attributes;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class GetModeratorUserInfoEvent(IModeratorUserInfoService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        moderation.Send(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
