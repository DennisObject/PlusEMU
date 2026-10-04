using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class GetModeratorUserRoomVisitsEvent(IModeratorHistoryService history) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var result = history.GetUserRoomVisits(packet.ReadInt());
        if (result != null) session.Send(new ModeratorUserRoomVisitsComposer(result));
        return Task.CompletedTask;
    }
}
