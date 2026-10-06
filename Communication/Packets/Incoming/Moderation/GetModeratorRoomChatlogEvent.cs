using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal class GetModeratorRoomChatlogEvent(IModeratorHistoryService history) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); //junk
        var result = history.GetRoomChatlog(packet.ReadUInt());

        if (result != null)
        {
            session.Send(new ModeratorRoomChatlogComposer(result));
        }

        return Task.CompletedTask;
    }
}
