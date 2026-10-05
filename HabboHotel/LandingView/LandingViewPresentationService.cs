using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.LandingView;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.LandingView.Promotions;

namespace Plus.HabboHotel.LandingView;

public sealed record LandingPromotionSnapshot(
    int Id, string Title, string Text, string ButtonText, int ButtonType, string ButtonLink, string ImageLink)
{
    public static LandingPromotionSnapshot Capture(Promotion promotion) => new(promotion.Id, promotion.Title,
        promotion.Text, promotion.ButtonText, promotion.ButtonType, promotion.ButtonLink, promotion.ImageLink);
}

public interface ILandingViewPresentationService
{
    void ShowArticles(GameClient session);
    void RefreshCampaign(GameClient session, string campaigns);
}

public sealed class LandingViewPresentationService(ILandingViewManager landingView) : ILandingViewPresentationService
{
    public void RefreshCampaign(GameClient session, string campaigns)
    {
        if (campaigns.Contains("gamesmaker")) return;
        var name = "";
        foreach (var value in campaigns.Split(';'))
        {
            if (string.IsNullOrEmpty(value) || value.EndsWith(",")) continue;
            var fields = value.Split(',');
            if (fields.Length < 2) return;
            name = fields[1];
        }
        try { session.Send(new CampaignComposer(campaigns, name)); }
        catch { /* Campaign refresh has always ignored publication failures. */ }
    }

    public void ShowArticles(GameClient session) =>
        session.Send(new PromoArticlesComposer(landingView.GetPromotionItems().Select(LandingPromotionSnapshot.Capture).ToImmutableArray()));
}
