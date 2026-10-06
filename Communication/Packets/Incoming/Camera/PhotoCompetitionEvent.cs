using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class PhotoCompetitionEvent(ICameraPhotoService photos) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        photos.EnterCompetition(session, CameraCheckoutPacket.ReadMediaId(packet));

        return Task.CompletedTask;
    }
}
