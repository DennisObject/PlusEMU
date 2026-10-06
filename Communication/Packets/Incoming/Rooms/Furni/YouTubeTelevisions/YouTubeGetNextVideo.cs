using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Televisions;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.YouTubeTelevisions;

internal class YouTubeGetNextVideo(ITelevisionPresentationService televisions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var itemId = packet.ReadInt();
        packet.ReadInt(); // next
        televisions.ShowNextVideo(session, itemId);

        return Task.CompletedTask;
    }
}
