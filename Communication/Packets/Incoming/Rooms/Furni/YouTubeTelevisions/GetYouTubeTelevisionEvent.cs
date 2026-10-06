using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Televisions;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.YouTubeTelevisions;

internal class GetYouTubeTelevisionEvent(ITelevisionPresentationService televisions) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        televisions.ShowPlaylist(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}
