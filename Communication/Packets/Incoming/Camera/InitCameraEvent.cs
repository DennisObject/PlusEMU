using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class InitCameraEvent(ICameraPhotoService photos) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (packet.Buffer.Length == 0)
        {
            photos.Initialize(session);
        }

        return Task.CompletedTask;
    }
}
