using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Rooms.Furni.YouTubeTelevisions;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.YouTubeTelevisions;
using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Televisions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class TelevisionPresentationTests
{
    [Fact]
    public async Task HandlerDecodesTheItemIdAndDelegates()
    {
        var presentation = new RecordingPresentation();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        await new GetYouTubeTelevisionEvent(presentation).Parse(client, HabbiconTestSupport.Incoming(42));

        Assert.Equal(new[] { 42 }, presentation.Shown);
        Assert.Empty(sent);
    }

    [Fact]
    public void PlaylistWritesEveryVideoInOrderWithTheTrailingEmptyString()
    {
        var videos = ImmutableArray.Create(new TelevisionVideoSnapshot("aaa", "Title A", "Desc A"), new TelevisionVideoSnapshot("bbb", "Title B", ""));
        var packet = new HabbiconTestSupport.RecordingPacket();

        new GetYouTubePlaylistComposer(7, videos).Compose(packet);

        Assert.Equal(new object[] { 7, 2, "aaa", "Title A", "Desc A", "bbb", "Title B", "", "" }, packet.Writes);
    }

    [Fact]
    public void EmptyPlaylistWritesOnlyTheItemAndATrailingString()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();

        new GetYouTubePlaylistComposer(7, ImmutableArray<TelevisionVideoSnapshot>.Empty).Compose(packet);

        Assert.Equal(new object[] { 7, 0, "" }, packet.Writes);
    }

    [Fact]
    public void PlaylistRecompositionIsStableAfterSourceListChanges()
    {
        var source = new List<TelevisionVideoSnapshot> { new("aaa", "Title A", "Desc A") };
        var composer = new GetYouTubePlaylistComposer(7, source.ToImmutableArray());
        var first = Write(composer);

        source.Clear();
        source.Add(new TelevisionVideoSnapshot("zzz", "Changed", "Changed"));

        Assert.Equal(first, Write(composer));
        Assert.Equal(first, Write(composer));
    }

    [Fact]
    public void ServiceOutsideARoomSendsNothing()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });

        new TelevisionPresentationService(Televisions(Video(1, "aaa", "Title A"))).ShowPlaylist(client, 7);

        Assert.Empty(sent);
    }

    [Fact]
    public void ServiceInARoomWithoutVideosSendsOnlyTheNotification()
    {
        var (client, sent) = InRoom(new Habbo { Id = 7 });

        new TelevisionPresentationService(Televisions()).ShowPlaylist(client, 7);

        var notification = Assert.Single(sent);
        Assert.NotEqual(ServerPacketHeader.GetYouTubeVideoComposer, notification.Header);
        Assert.NotEqual(ServerPacketHeader.GetYouTubePlaylistComposer, notification.Header);
    }

    [Fact]
    public void ServiceSendsARandomVideoBeforeThePlaylist()
    {
        var televisions = Televisions(Video(1, "aaa", "Title A"), Video(2, "bbb", "Title B"));
        var (client, sent) = InRoom(new Habbo { Id = 7 });

        new TelevisionPresentationService(televisions).ShowPlaylist(client, 7);

        Assert.Equal(new[] { ServerPacketHeader.GetYouTubeVideoComposer, ServerPacketHeader.GetYouTubePlaylistComposer }, sent.Select(packet => packet.Header));
        var video = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(7, video.ReadInt());
        Assert.Contains(video.ReadString(), new[] { "aaa", "bbb" });
        var playlist = new FlashIncomingPacket { Buffer = sent[1].Payload };
        Assert.Equal((7, 2, "aaa", "Title A"), (playlist.ReadInt(), playlist.ReadInt(), playlist.ReadString(), playlist.ReadString()));
    }

    private static (GameClient Client, List<(uint Header, byte[] Payload)> Sent) InRoom(Habbo habbo)
    {
        var room = new Room(Data(1), Array.Empty<IRoomComponent>(), TestLogging.Navigation, TestLogging.Logger);
        habbo.CurrentRoom = room;
        return HabbiconTestSupport.Client(habbo);
    }

    private static RoomData Data(uint id)
    {
        var data = (RoomData)RuntimeHelpers.GetUninitializedObject(typeof(RoomData));
        data.Id = id;
        return data;
    }

    private static TelevisionItem Video(int id, string youTubeId, string title) => new(id, youTubeId, title, "", true);

    private static FakeTelevisions Televisions(params TelevisionItem[] items)
    {
        var televisions = new FakeTelevisions();
        foreach (var item in items)
            televisions.Televisions[item.Id] = item;
        return televisions;
    }

    private static List<object> Write(Plus.Communication.Packets.IServerPacket composer)
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(packet);
        return packet.Writes;
    }

    private sealed class RecordingPresentation : ITelevisionPresentationService
    {
        public List<int> Shown { get; } = new();
        public void ShowPlaylist(GameClient session, int itemId) => Shown.Add(itemId);
    }

    private sealed class FakeTelevisions : ITelevisionManager
    {
        public Dictionary<int, TelevisionItem> Televisions { get; } = new();
        public ICollection<TelevisionItem> TelevisionList => Televisions.Values;
        public void Init() { }
        public bool TryGet(int itemId, out TelevisionItem? televisionItem) => Televisions.TryGetValue(itemId, out televisionItem);
    }
}
