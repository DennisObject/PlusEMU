using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Settings;

internal class UnbanUserFromRoomEvent(IRoomModerationService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        moderation.Unban(session, packet.ReadInt(), packet.ReadInt());
        return Task.CompletedTask;
    }
}