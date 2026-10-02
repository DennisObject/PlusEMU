using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Quests;

public sealed class RewardTrackProgressComposer : IServerPacket
{
    private readonly string _trackId;
    private readonly string _taskId;
    private readonly int _count;
    private readonly int _points;
    public uint MessageId => ServerPacketHeader.RewardTrackProgressComposer;

    public RewardTrackProgressComposer(string trackId, string taskId, int count, int points)
    {
        _trackId = trackId ?? "";
        _taskId = taskId ?? "";
        _count = count;
        _points = points;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_trackId);
        packet.WriteString(_taskId);
        packet.WriteInteger(_count);
        packet.WriteInteger(_points);
    }
}
