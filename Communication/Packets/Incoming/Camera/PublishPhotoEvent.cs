using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class PublishPhotoEvent(ICameraPhotoService photos) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        photos.Publish(session, CameraCheckoutPacket.ReadMediaId(packet));

        return Task.CompletedTask;
    }
}
