using Microsoft.Extensions.Logging;
using MySqlConnector;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.Communication.Packets.Outgoing.Habbicons;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Habbicons;

namespace Plus.Communication.Packets.Incoming.Habbicons;

// Concrete handlers retain PacketManager's convention of one type per header.
public abstract class HabbiconRequest(IHabbiconService service, ILogger<HabbiconRequest> logger) : IPacketEvent
{
    protected virtual HabbiconAction? Action => null;
    protected virtual bool Info => false;
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        bool purchase = Action is HabbiconAction.Buy or HabbiconAction.BuyCollection or HabbiconAction.Claim;
        int id = Action != null || Info ? packet.ReadInt() : 0;
        try
        {
            if (Action is { } action)
            {
                HabbiconMessages.Publish(session, service.Change(session.GetHabbo(), action, id));
                if (purchase) session.Send(new PurchaseOkComposer());
            }
            else if (Info) session.Send(new HabbiconInfoComposer(service.Load(session.GetHabbo().Id).RequireItem(id)));
            else
            {
                var snapshot = service.Load(session.GetHabbo().Id);
                HabbiconMessages.SendSnapshot(session, snapshot);
                session.Send(new HabbiconShopDataComposer(snapshot));
            }
        }
        catch (HabbiconRejected rejected)
        {
            if (purchase) session.Send(new PurchaseErrorComposer(rejected.Code));
        }
        catch (MySqlException exception)
        {
            logger.LogError(exception, "Unable to process Habbicon request for {UserId}", session.GetHabbo().Id);
            if (purchase) session.Send(new PurchaseErrorComposer(5));
        }
        return Task.CompletedTask;
    }
}
