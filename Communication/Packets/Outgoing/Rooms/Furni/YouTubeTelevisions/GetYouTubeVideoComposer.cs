using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.YouTubeTelevisions;

public class GetYouTubeVideoComposer : IServerPacket
{
    private readonly int _itemId;
    private readonly string _youTubeVideo;

    public uint MessageId => ServerPacketHeader.GetYouTubeVideoComposer;

    public GetYouTubeVideoComposer(int itemId, string youTubeVideo)
    {
        _itemId = itemId;
        _youTubeVideo = youTubeVideo;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(_itemId);
        packet.WriteString(_youTubeVideo); //"dQw4w9WgXcQ");
        packet.WriteInteger(0); //Start seconds
        packet.WriteInteger(0); //End seconds
        packet.WriteInteger(0); //State

    }
}
