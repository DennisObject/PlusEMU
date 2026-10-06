using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Chat;

public class StartTypingEvent(IRoomAvatarActionService actions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        actions.SetTyping(session, true);

        return Task.CompletedTask;
    }
}
