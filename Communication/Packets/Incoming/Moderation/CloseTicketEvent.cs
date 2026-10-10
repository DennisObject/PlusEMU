using Plus.Communication.Attributes;
using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;

namespace Plus.Communication.Packets.Incoming.Moderation;

[RequiresPermission(PermissionKeys.ModerationTool)]
internal sealed class CloseTicketEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var resolution = packet.ReadInt();
        var count = packet.ReadInt();

        if (resolution < 1 || resolution > 3 || count < 0 || (long)count * 4 > packet.Buffer.Length) {
            throw new ArgumentOutOfRangeException(nameof(packet));
        }

        if (count == 0) {
            return;
        }

        var ids = new int[count];

        for (var index = 0; index < count; index++) {
            ids[index] = packet.ReadInt();
        }

        if (new HashSet<int>(ids).Count != ids.Length) {
            throw new ArgumentOutOfRangeException(nameof(packet));
        }

        var result = (SupportTicketResult)resolution;

        foreach (var id in ids) {
            tickets.Close(session, id, result);
        }
    }
}
