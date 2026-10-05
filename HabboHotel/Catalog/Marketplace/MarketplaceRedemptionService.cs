using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.HabboHotel.Catalog.Marketplace;

public interface IMarketplaceRedemptionService
{
    void Redeem(GameClient session);
}

public sealed class MarketplaceRedemptionService(IMarketplaceOfferStore store) : IMarketplaceRedemptionService
{
    public void Redeem(GameClient session)
    {
        var habbo = session.GetHabbo();
        lock (habbo.WalletSync)
        {
            // A wallet saved for shutdown cannot receive credits; the sold offers stay queued for the next login.
            if (habbo.WalletClosed) return;
            // The store commits the claim only if the wallet can hold the total, so no sale is removed without being paid.
            var owed = store.ClaimSold(habbo.Id, amount => HousekeepingLimits.AddToBalance(habbo.Credits, amount) != null);
            if (owed is not int total || total < 1) return;
            habbo.Credits += total;
            session.Send(new CreditBalanceComposer(habbo.Credits));
        }
    }
}
