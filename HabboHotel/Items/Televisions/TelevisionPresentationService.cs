using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.YouTubeTelevisions;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Items.Televisions;

public interface ITelevisionPresentationService
{
    void ShowPlaylist(GameClient session, int itemId);
    void ShowNextVideo(GameClient session, int itemId);
}

public sealed class TelevisionPresentationService(ITelevisionManager televisions) : ITelevisionPresentationService
{
    public void ShowNextVideo(GameClient session, int itemId)
    {
        if (!session.GetHabbo().InRoom)
            return;
        if (televisions.TelevisionList.Count == 0)
        {
            session.SendNotification("Oh, it looks like the hotel manager haven't added any videos for you to watch! :(");
            return;
        }
        var candidates = televisions.Televisions.Values.ToList();
        session.Send(new GetYouTubeVideoComposer(itemId, candidates[Random.Shared.Next(candidates.Count)].YouTubeId));
    }

    public void ShowPlaylist(GameClient session, int itemId)
    {
        if (!session.GetHabbo().InRoom)
            return;
        var playlist = televisions.TelevisionList.Select(TelevisionVideoSnapshot.Capture).ToImmutableArray();
        if (playlist.IsEmpty)
        {
            session.SendNotification("Oh, it looks like the hotel manager haven't added any videos for you to watch! :(");
            return;
        }
        // A random video is shown first, then the whole playlist, as the client expects.
        var candidates = televisions.Televisions.Values.ToList();
        session.Send(new GetYouTubeVideoComposer(itemId, candidates[Random.Shared.Next(candidates.Count)].YouTubeId));
        session.Send(new GetYouTubePlaylistComposer(itemId, playlist));
    }
}
