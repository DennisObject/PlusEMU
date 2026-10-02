using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Camera;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Camera;

public sealed class PurchasePhotoEvent(ICameraService camera, ICameraCheckoutService checkout, IAchievementManager achievements, ILogger<PurchasePhotoEvent> logger) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        try
        {
            var result = CameraCheckoutPacket.Execute(session, packet, camera, media => checkout.Purchase(session.GetHabbo(), media));
            if (!result.Ok || result.Item == null)
            {
                session.SendNotification("The photograph could not be purchased. No payment was taken.");
                return Task.CompletedTask;
            }
            var habbo = session.GetHabbo();
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
        return Task.CompletedTask;
    }
}
