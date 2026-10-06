using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

internal sealed class SubmitNewTicketEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var message = packet.ReadString();
        var category = packet.ReadInt();
        var reportedId = packet.ReadInt();
        var type = packet.ReadInt();
        var count = packet.ReadInt();
        var chats = ImmutableArray.CreateBuilder<string>();

        for (var index = 0; index < count; index++) {
            packet.ReadInt();
            chats.Add(packet.ReadString());
        }

        tickets.Submit(session, new(message, category, reportedId, type, chats.ToImmutable()));

        return Task.CompletedTask;
    }
}
