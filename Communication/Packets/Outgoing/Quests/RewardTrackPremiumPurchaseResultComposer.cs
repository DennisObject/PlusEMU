using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class RewardTrackPremiumPurchaseResultComposer : IServerPacket
{
    private readonly string _trackId;
    private readonly Plus.HabboHotel.Quests.RewardTrackResults _resultCode;
    private readonly int _points;
    public uint MessageId => ServerPacketHeader.RewardTrackPremiumPurchaseResultComposer;

    public RewardTrackPremiumPurchaseResultComposer(string trackId, Plus.HabboHotel.Quests.RewardTrackResults resultCode, int points)
    {
        _trackId = trackId ?? "";
        _resultCode = resultCode;
        _points = points;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_trackId);
        packet.WriteInteger((int)_resultCode);
        packet.WriteInteger(_points);
    }
}
