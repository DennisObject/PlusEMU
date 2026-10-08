using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class InitCameraEvent(ICameraPhotoService photos) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (packet.Buffer.Length == 0) {
            photos.Initialize(session);

            return Task.CompletedTask;
        }

        // An opening camera adds its viewport as one bounded string; anything else is not a camera request.
        var payload = CameraPacketDecoder.Decode(packet);

        if (payload.Json != null) {
            photos.Initialize(session, payload.Json);
        }

        return Task.CompletedTask;
    }
}
