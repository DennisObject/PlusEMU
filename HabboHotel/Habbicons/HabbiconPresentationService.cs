using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Habbicons;

public interface IHabbiconPresentationService
{
    void ShowShop(GameClient session);
    void ShowInfo(GameClient session, int id);
    void Change(GameClient session, HabbiconAction action, int id);
}

public sealed class HabbiconPresentationService(IHabbiconService habbicons,
    ILogger<HabbiconPresentationService> logger) : IHabbiconPresentationService
{
    public void ShowShop(GameClient session) => Execute(session, false, () =>
    {
        var snapshot = habbicons.Load(session.GetHabbo().Id);
        HabbiconMessages.SendSnapshot(session, snapshot);
        session.Send(new HabbiconShopDataComposer(snapshot));
    });

    public void ShowInfo(GameClient session, int id) => Execute(session, false, () =>
        session.Send(new HabbiconInfoComposer(habbicons.Load(session.GetHabbo().Id).RequireItem(id))));

    public void Change(GameClient session, HabbiconAction action, int id)
    {
        var purchase = action is HabbiconAction.Buy or HabbiconAction.BuyCollection or HabbiconAction.Claim;
        Execute(session, purchase, () =>
        {
            HabbiconMessages.Publish(session, habbicons.Change(session.GetHabbo(), action, id));
            if (purchase)
                session.Send(new PurchaseOKComposer());
        });
    }

    private void Execute(GameClient session, bool purchase, Action operation)
    {
        try
        {
            operation();
        }
        catch (HabbiconRejected rejected)
        {
            if (purchase)
                session.Send(new PurchaseErrorComposer((PurchaseError)rejected.Code));
        }
        catch (MySqlException exception)
        {
            logger.LogError(exception, "Unable to process Habbicon request for {UserId}", session.GetHabbo().Id);
            if (purchase)
                session.Send(new PurchaseErrorComposer(PurchaseError.DeliveryFailed));
        }
    }
}
