using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal sealed class MuteUserEvent(IRoomMuteService mutes) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        packet.ReadInt(); // roomId
        var durationMinutes = packet.ReadInt();
        mutes.Mute(session, userId, durationMinutes);

        return Task.CompletedTask;
    }
}
