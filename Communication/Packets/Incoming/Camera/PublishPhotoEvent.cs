using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class PublishPhotoEvent(ICameraService camera, ICameraCheckoutService checkout, ILogger<PublishPhotoEvent> logger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        try
        {
            string url = "";
            var result = CameraCheckoutPacket.Execute(session, packet, camera, media =>
            {
                url = CameraMediaPath.For(media.Id);
                return checkout.Publish(session.GetHabbo(), media);
            });
            session.Send(new CameraPublishStatusComposer(result.Ok, result.WaitSeconds, url));
            if (result.Changed) session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, 0));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera publication failed for session {SessionId}", session.Id);
            session.Send(new CameraPublishStatusComposer(false, 0));
        }
        return Task.CompletedTask;
    }
}
