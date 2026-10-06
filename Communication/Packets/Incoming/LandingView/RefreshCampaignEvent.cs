using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.LandingView;

namespace Plus.Communication.Packets.Incoming.LandingView;

internal class RefreshCampaignEvent(ILandingViewPresentationService landingView) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        string campaigns;

        try {
            campaigns = packet.ReadString();
        }
        catch {
            return Task.CompletedTask;
        }

        landingView.RefreshCampaign(session, campaigns);

        return Task.CompletedTask;
    }
}
