using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class PhotoCompetitionEvent(ICameraService camera, ICameraCheckoutService checkout, ILogger<PhotoCompetitionEvent> logger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        try
        {
            var result = CameraCheckoutPacket.Execute(session, packet, camera, media => checkout.EnterCompetition(session.GetHabbo(), media));
            var reason = result.Error switch { "limit" => "too-many-submits", "email" => "email-not-verified", _ => result.Error };
            session.Send(new CompetitionStatusComposer(result.Ok, reason));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera competition entry failed for session {SessionId}", session.Id);
            session.Send(new CompetitionStatusComposer(false, "unavailable"));
        }
        return Task.CompletedTask;
    }
}
