using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.LandingView;

namespace Plus.Communication.Packets.Incoming.LandingView;

internal class GetPromoArticlesEvent(ILandingViewPresentationService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.ShowArticles(session);
        return Task.CompletedTask;
    }
}