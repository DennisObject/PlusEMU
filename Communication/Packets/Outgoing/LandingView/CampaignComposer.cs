using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.LandingView;

public class CampaignComposer : IServerPacket
{
    private readonly string _campaignString;
    private readonly string _campaignName;

    public uint MessageId => ServerPacketHeader.CampaignComposer;

    public CampaignComposer(string campaignString, string campaignName)
    {
        _campaignString = campaignString;
        _campaignName = campaignName;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_campaignString);
        packet.WriteString(_campaignName);
    }
}
