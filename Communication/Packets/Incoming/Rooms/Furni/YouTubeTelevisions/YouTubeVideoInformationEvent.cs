using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Televisions;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.YouTubeTelevisions;

internal class YouTubeVideoInformationEvent(ITelevisionPresentationService televisions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadInt();
        var videoId = packet.ReadString();
        televisions.ShowVideoInformation(session, itemId, videoId);

        return Task.CompletedTask;
    }
}
