using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal class ToggleMuteToolEvent(IRoomModerationService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        moderation.ToggleMute(session);

        return Task.CompletedTask;
    }
}
