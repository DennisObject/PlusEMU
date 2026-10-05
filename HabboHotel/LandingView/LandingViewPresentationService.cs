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
}

public sealed class LandingViewPresentationService(ILandingViewManager landingView) : ILandingViewPresentationService
{
    public void ShowArticles(GameClient session) =>
        session.Send(new PromoArticlesComposer(landingView.GetPromotionItems().Select(LandingPromotionSnapshot.Capture).ToImmutableArray()));
}
