using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Misc;

public class VideoOffersRewardsComposer : IServerPacket
{
    private readonly int _id;
    private readonly string _type;
    private readonly string _message;

    public uint MessageId => ServerPacketHeader.VideoOffersRewardsComposer;

    public VideoOffersRewardsComposer(int id, string type, string message)
    {
        _id = id;
        _type = type;
        _message = message;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(_type);
        packet.WriteInteger(_id);
        packet.WriteString(_message);
        packet.WriteString("");
    }
}
