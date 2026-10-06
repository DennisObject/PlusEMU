using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Televisions;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.YouTubeTelevisions;

public class GetYouTubePlaylistComposer(int itemId, ImmutableArray<TelevisionVideoSnapshot> videos) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.GetYouTubePlaylistComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(itemId);
        packet.WriteInteger(videos.Length);

        foreach (var video in videos) {
            packet.WriteString(video.YouTubeId);
            packet.WriteString(video.Title); //Title
            packet.WriteString(video.Description); //Description
        }

        packet.WriteString("");
    }
}
