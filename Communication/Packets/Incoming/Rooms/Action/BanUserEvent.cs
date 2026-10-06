using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class BanUserEvent(IRoomModerationService moderation) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var request = new RoomBanRequest(packet.ReadInt(), packet.ReadInt(), packet.ReadString());
        moderation.Ban(session, request);

        return Task.CompletedTask;
    }
}
