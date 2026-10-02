using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class RewardTrackClaimResultComposer : IServerPacket
{
    private readonly string _trackId;
    private readonly string _rewardId;
    private readonly int _resultCode;
    public uint MessageId => ServerPacketHeader.RewardTrackClaimResultComposer;

    public RewardTrackClaimResultComposer(string trackId, string rewardId, int resultCode)
    {
        _trackId = trackId ?? "";
        _rewardId = rewardId ?? "";
        _resultCode = resultCode;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_trackId);
        packet.WriteString(_rewardId);
        packet.WriteInteger(_resultCode);
    }
}
