using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Camera;

[Singleton]
public interface ICameraPhotoService
{
    void Initialize(GameClient session);
    void Purchase(GameClient session, Guid? mediaId);
    void Publish(GameClient session, Guid? mediaId);
    void EnterCompetition(GameClient session, Guid? mediaId);
}

public sealed class CameraPhotoService(ICameraService camera, ICameraCheckoutService checkout,
    IAchievementManager achievements, IRewardTrackManager rewards, ILogger<CameraPhotoService> logger) : ICameraPhotoService
{
    public void Initialize(GameClient session)
    {
        if (!session.IsAuthenticated)
        {
            return;
        }

        var prices = (Credits: 0, Points: 0, PublishPoints: 0);

        try
        {
            if (checkout.Enabled)
            {
                prices = checkout.Prices;
            }
        }
        catch (Exception)
        {
            // Invalid camera pricing keeps the established zero-price initialization response.
        }

        session.Send(new InitCameraComposer(prices.Credits, prices.Points, prices.PublishPoints));
    }

    public void Purchase(GameClient session, Guid? mediaId)
    {
        try
        {
            var habbo = session.GetHabbo();
            var result = Execute(session, habbo, mediaId, media => checkout.Purchase(habbo, media));

            if (!result.Ok || result.Item == null)
            {
                session.SendNotification("The photograph could not be purchased. No payment was taken.");

                return;
            }

            habbo.Inventory.Furniture.AddItem(result.Item);
            session.Send(new FurniListNotificationComposer(result.Item.Id, 1));
            session.Send(new FurniListUpdateComposer());
            session.Send(new CreditBalanceComposer(habbo.Credits));
            session.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, 0));
            session.Send(new CameraPurchaseOKComposer());
            achievements.ProgressAchievement(session, "ACH_CameraPhotoCount", 1);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera purchase failed for session {SessionId}", session.Id);
            session.SendNotification("The photograph purchase could not be completed.");
        }
    }

    public void Publish(GameClient session, Guid? mediaId)
    {
        try
        {
            var habbo = session.GetHabbo();
            var url = "";
            var result = Execute(session, habbo, mediaId, media =>
            {
                url = CameraMediaPath.For(media.Id);

                return checkout.Publish(habbo, media);
            });
            session.Send(new CameraPublishStatusComposer(result.Ok, result.WaitSeconds, url));

            if (result.Ok && result.Changed)
            {
                session.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, 0));
                rewards.Progress(session, RewardTrackActions.PublishPicture);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera publication failed for session {SessionId}", session.Id);
            session.Send(new CameraPublishStatusComposer(false, 0));
        }
    }

    public void EnterCompetition(GameClient session, Guid? mediaId)
    {
        try
        {
            var habbo = session.GetHabbo();
            var result = Execute(session, habbo, mediaId, media => checkout.EnterCompetition(habbo, media));
            var reason = result.Error switch
            {
                "limit" => "too-many-submits",
                "email" => "email-not-verified",
                _ => result.Error
            };
            session.Send(new CompetitionStatusComposer(result.Ok, reason));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Camera competition entry failed for session {SessionId}", session.Id);
            session.Send(new CompetitionStatusComposer(false, "unavailable"));
        }
    }

    private CameraCheckoutResult Execute(GameClient session, Habbo? habbo, Guid? mediaId,
        Func<CameraCheckoutMedia, CameraCheckoutResult> operation)
    {
        if (!session.IsAuthenticated || habbo?.CurrentRoom == null || mediaId is not { } id)
        {
            return new(false, "unavailable");
        }

        return camera.Checkout(session, id, operation);
    }
}
