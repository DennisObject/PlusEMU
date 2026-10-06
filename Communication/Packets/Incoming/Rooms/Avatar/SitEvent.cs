using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Rooms.Avatar;

internal class SitEvent(IRoomAvatarActionService actions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        actions.SetPosture(session, packet.ReadInt());

        return Task.CompletedTask;
    }
}
