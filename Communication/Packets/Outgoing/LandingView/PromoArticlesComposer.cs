using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.LandingView;

namespace Plus.Communication.Packets.Outgoing.LandingView;

public class PromoArticlesComposer : IServerPacket
{
    private readonly ImmutableArray<LandingPromotionSnapshot> _landingPromotions;
    public uint MessageId => ServerPacketHeader.PromoArticlesComposer;

    public PromoArticlesComposer(ImmutableArray<LandingPromotionSnapshot> landingPromotions)
    {
        _landingPromotions = landingPromotions;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_landingPromotions.Length); //Count
        foreach (var promotion in _landingPromotions)
        {
            packet.WriteInteger(promotion.Id); //ID
            packet.WriteString(promotion.Title); //Title
            packet.WriteString(promotion.Text); //Text
            packet.WriteString(promotion.ButtonText); //Button text
            packet.WriteInteger(promotion.ButtonType); //Link type 0 and 3
            packet.WriteString(promotion.ButtonLink); //Link to article
            packet.WriteString(promotion.ImageLink); //Image link
        }
    }
}