using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class GetModeratorUserChatlogEvent(IModeratorHistoryService history) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var result = history.GetUserChatlog(packet.ReadInt());
        if (result == null)
        {
            session.SendNotification("Unable to load info for user.");
            return Task.CompletedTask;
        }
        session.Send(new ModeratorUserChatlogComposer(result));
        return Task.CompletedTask;
    }
}
