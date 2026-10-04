using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.Packets.Incoming.Moderation;

internal sealed class CallForHelpPendingCallsDeletedEvent(IModeratorTicketService tickets) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        tickets.DeletePending(session);
        return Task.CompletedTask;
    }
}
