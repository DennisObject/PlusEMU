using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

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
            if (result.Ok && result.Changed)
            {
                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, 0));
                RewardTrackManager.Current?.Progress(session, RewardTrackActions.PublishPicture);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera publication failed for session {SessionId}", session.Id);
            session.Send(new CameraPublishStatusComposer(false, 0));
        }
        return Task.CompletedTask;
    }
}
