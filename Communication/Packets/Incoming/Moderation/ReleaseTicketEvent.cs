using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal sealed class ReleaseTicketEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var count = packet.ReadInt();
        var ids = new List<int>();

        for (var index = 0; index < count; index++) {
            ids.Add(packet.ReadInt());
        }

        tickets.Release(session, ids);

        return Task.CompletedTask;
    }
}
