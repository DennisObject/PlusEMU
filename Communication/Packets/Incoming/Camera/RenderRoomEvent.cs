using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class RenderRoomEvent(ICameraService camera) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => camera.Handle(session, packet, false);
}
