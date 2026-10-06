using Plus.Communication.Packets.Outgoing.Rooms.Furni.YouTubeTelevisions;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.YouTubeTelevisions;

internal class ToggleYouTubeVideoEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadInt(); //Item Id
        var videoId = packet.ReadString(); //Video ID
        session.Send(new GetYouTubeVideoComposer(itemId, videoId));

        return Task.CompletedTask;
    }
}
