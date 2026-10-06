using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Navigator;

public class RoomRatingComposer : IServerPacket
{
    private readonly int _score;
    private readonly bool _canVote;

    public uint MessageId => ServerPacketHeader.RoomRatingComposer;

    public RoomRatingComposer(int score, bool canVote)
    {
        _score = score;
        _canVote = canVote;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_score);
        packet.WriteBoolean(_canVote);
    }
}
