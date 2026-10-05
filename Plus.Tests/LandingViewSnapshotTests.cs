using System.Collections.Immutable;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.LandingView;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.LandingView;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.LandingView;
using Plus.HabboHotel.LandingView.Promotions;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class LandingViewSnapshotTests
{
    [Fact]
    public void ArticlesWriteCapturedFieldsAfterSourceModelAndCollectionMutation()
    {
        var promotion = new Promotion { Id = 7, Title = "title", Text = "body", ButtonText = "button", ButtonType = 3, ButtonLink = "link", ImageLink = "image" };
        var source = new List<Promotion> { promotion };
        var composer = new PromoArticlesComposer(source.Select(LandingPromotionSnapshot.Capture).ToImmutableArray());
        promotion.Id = 99;
        promotion.Title = "changed";
        promotion.Text = "changed";
        promotion.ButtonText = "changed";
        promotion.ButtonType = 0;
        promotion.ButtonLink = "changed";
        promotion.ImageLink = "changed";
        source.Clear();
        for (var index = 0; index < 2; index++)
        {
            var packet = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(packet);
            Assert.Equal(new object[] { 1, 7, "title", "body", "button", 3, "link", "image" }, packet.Writes);
        }
    }

    [Fact]
    public async Task HandlerOnlyDelegatesAndServiceSendsEmptyAndPopulatedLists()
    {
        var recording = new RecordingService();
        await new GetPromoArticlesEvent(recording).Parse(null!, HabbiconTestSupport.Incoming());
        Assert.True(recording.Shown);

        var source = new List<Promotion>();
        var manager = CatalogSnapshotTestSupport.Proxy<ILandingViewManager>((method, _) => method == "GetPromotionItems"
            ? source : throw new NotSupportedException(method));
        var service = new LandingViewPresentationService(manager);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        service.ShowArticles(client);
        Assert.Equal(ServerPacketHeader.PromoArticlesComposer, Assert.Single(sent).Header);
        Assert.Equal(0, new FlashIncomingPacket { Buffer = sent[0].Payload }.ReadInt());
        source.Add(new Promotion { Id = 7, Title = "title", Text = "body", ButtonText = "button", ButtonType = 3, ButtonLink = "link", ImageLink = "image" });
        sent.Clear();
        service.ShowArticles(client);
        var packet = new FlashIncomingPacket { Buffer = Assert.Single(sent).Payload };
        Assert.Equal(1, packet.ReadInt());
        Assert.Equal(7, packet.ReadInt());
        Assert.Equal("title", packet.ReadString());
        Assert.Equal("body", packet.ReadString());
        Assert.Equal("button", packet.ReadString());
        Assert.Equal(3, packet.ReadInt());
        Assert.Equal("link", packet.ReadString());
        Assert.Equal("image", packet.ReadString());
        Assert.False(packet.HasDataRemaining());
    }

    private sealed class RecordingService : ILandingViewPresentationService
    {
        public bool Shown { get; private set; }
        public void ShowArticles(GameClient session) => Shown = true;
    }
}
