using Microsoft.Extensions.Logging.Abstractions;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.Camera;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Incoming.LandingView;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Camera;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.LandingView;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class RemainingPresentationHandlerTests
{
    [Fact]
    public async Task HandlersDelegateDecodedPrimitivesWithoutReadingDomainState()
    {
        var groupCalls = 0;
        var groups = CatalogSnapshotTestSupport.Proxy<IGroupPresentationService>((method, _) =>
        {
            Assert.Equal("ShowCatalogFurnitureConfiguration", method);
            groupCalls++;
            return null;
        });
        await new GetGroupFurniConfigEvent(groups).Parse(null!, HabbiconTestSupport.Incoming());
        Assert.Equal(1, groupCalls);

        var campaigns = new List<string>();
        var landing = CatalogSnapshotTestSupport.Proxy<ILandingViewPresentationService>((method, args) =>
        {
            Assert.Equal("RefreshCampaign", method);
            campaigns.Add((string)args[1]);
            return null;
        });
        await new RefreshCampaignEvent(landing).Parse(null!, HabbiconTestSupport.Incoming("id,name;"));
        await new RefreshCampaignEvent(landing).Parse(null!, HabbiconTestSupport.Incoming());
        Assert.Equal(new[] { "id,name;" }, campaigns);

        var cameraCalls = 0;
        var photos = CatalogSnapshotTestSupport.Proxy<ICameraPhotoService>((method, _) =>
        {
            Assert.Equal("Initialize", method);
            cameraCalls++;
            return null;
        });
        await new InitCameraEvent(photos).Parse(null!, HabbiconTestSupport.Incoming());
        await new InitCameraEvent(photos).Parse(null!, HabbiconTestSupport.Incoming(1));
        Assert.Equal(1, cameraCalls);
    }

    [Theory]
    [InlineData("a,first;b,last;", "last")]
    [InlineData(";a,;", "")]
    [InlineData("", "")]
    public void CampaignRetainsTheLastUsableNameAndOriginalString(string campaigns, string name)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        new LandingViewPresentationService(null!).RefreshCampaign(client, campaigns);
        Assert.Equal(ServerPacketHeader.CampaignComposer, Assert.Single(sent).Header);
        var packet = new FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(campaigns, packet.ReadString());
        Assert.Equal(name, packet.ReadString());
        Assert.Empty(packet.Buffer.ToArray());
    }

    [Theory]
    [InlineData("gamesmaker,a")]
    [InlineData("a,first;malformed")]
    public void RejectedCampaignPublishesNothing(string campaigns)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        new LandingViewPresentationService(null!).RefreshCampaign(client, campaigns);
        Assert.Empty(sent);
    }

    [Fact]
    public void CampaignPublicationFailureKeepsTheExistingSilentPolicy()
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo());
        client.SendCallback = _ => throw new InvalidOperationException("send failed");
        new LandingViewPresentationService(null!).RefreshCampaign(client, "id,name");
    }

    [Theory]
    [InlineData(false, false, 0, 0, 0)]
    [InlineData(true, false, 10, 20, 30)]
    [InlineData(true, true, 0, 0, 0)]
    public void CameraInitializesFromEnabledPricesOrTheEstablishedZeroFallback(bool enabled, bool invalid, int credits, int points, int publish)
    {
        var reads = 0;
        var checkout = CatalogSnapshotTestSupport.Proxy<ICameraCheckoutService>((method, _) =>
        {
            reads++;
            return method switch
            {
                "get_Enabled" => enabled,
                "get_Prices" => invalid ? throw new InvalidOperationException("invalid price") : (10, 20, 30),
                _ => throw new NotSupportedException(method)
            };
        });
        var service = new CameraPhotoService(null!, checkout, null!, null!, NullLogger<CameraPhotoService>.Instance);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        client.IsAuthenticated = false;
        service.Initialize(client);
        Assert.Empty(sent);
        Assert.Equal(0, reads);
        client.IsAuthenticated = true;
        service.Initialize(client);
        Assert.Equal(ServerPacketHeader.InitCameraComposer, Assert.Single(sent).Header);
        var packet = new FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(new[] { credits, points, publish }, new[] { packet.ReadInt(), packet.ReadInt(), packet.ReadInt() });
        Assert.Empty(packet.Buffer.ToArray());
        Assert.Equal(enabled ? 2 : 1, reads);
    }

    [Fact]
    public void GroupFurnitureConfigurationCapturesMembershipFieldsAndBothColourModes()
    {
        var group = new Group(7, "Group", "Description", "badge", 42, 1, DateTimeOffset.UnixEpoch,
            0, 1, 2, 0, false, new GroupMembershipSnapshot([], [], []));
        var colours = new List<(int, bool)>();
        var groups = CatalogSnapshotTestSupport.Proxy<IGroupManager>((method, args) => method switch
        {
            "GetGroupsForUser" => (int)args[0] == 1 ? new List<Group> { group } : new List<Group>(),
            "GetColourCode" => Colour((int)args[0], (bool)args[1]),
            _ => throw new NotSupportedException(method)
        });
        string Colour(int id, bool primary) { colours.Add((id, primary)); return primary ? "primary" : "secondary"; }
        var service = new GroupPresentationService(groups, null!, null!, null!, null!);
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        service.ShowCatalogFurnitureConfiguration(client);
        Assert.Equal(ServerPacketHeader.GroupFurniConfigComposer, Assert.Single(sent).Header);
        Assert.Equal(new[] { (1, true), (2, false) }, colours);
        var packet = new FlashIncomingPacket { Buffer = sent[0].Payload };
        Assert.Equal(1, packet.ReadInt()); Assert.Equal(7, packet.ReadInt());
        Assert.Equal("Group", packet.ReadString()); Assert.Equal("badge", packet.ReadString());
        Assert.Equal("primary", packet.ReadString()); Assert.Equal("secondary", packet.ReadString());
        Assert.False(packet.ReadBool()); Assert.Equal(1, packet.ReadInt()); Assert.False(packet.ReadBool());
        Assert.Empty(packet.Buffer.ToArray());
        sent.Clear();
        var (other, empty) = HabbiconTestSupport.Client(new Habbo { Id = 2 });
        service.ShowCatalogFurnitureConfiguration(other);
        Assert.Equal(0, new FlashIncomingPacket { Buffer = Assert.Single(empty).Payload }.ReadInt());
    }
}
