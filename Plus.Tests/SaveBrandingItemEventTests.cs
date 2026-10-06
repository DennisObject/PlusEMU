using System.Collections.Immutable;
using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class SaveBrandingItemEventTests
{
    [Fact]
    public async Task IdOnlyFrameDelegatesWithoutAMap()
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var metadata = new RecordingBranding();

        await new SaveBrandingItemEvent(metadata).Parse(client, HabbiconTestSupport.Incoming(7));

        var request = Assert.Single(metadata.Requests);
        Assert.Equal((7u, (ImmutableArray<string>?)null), (request.ItemId, request.Values));
    }

    [Fact]
    public async Task FullFrameDelegatesEveryPairInOrderIncludingAnEmptyMap()
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var metadata = new RecordingBranding();
        var events = new SaveBrandingItemEvent(metadata);

        await events.Parse(client, HabbiconTestSupport.Incoming(7, 4, "a", "1", "b", "2"));
        await events.Parse(client, HabbiconTestSupport.Incoming(8, 0));

        Assert.Equal(new[] { "a", "1", "b", "2" }, metadata.Requests[0].Values!.Value.ToArray());
        Assert.Equal(new string[] { }, metadata.Requests[1].Values!.Value.ToArray());
    }

    public static IEnumerable<object[]> MalformedFrames()
    {
        yield return new object[] { new object[] { 7, 3, "a", "1", "b" } };
        yield return new object[] { new object[] { 7, -2 } };
        yield return new object[] { new object[] { 7, 130 } };
        yield return new object[] { new object[] { 7, 2, "a", "1", "trailing" } };
        yield return new object[] { new object[] { 7, 4, "a", "1", "b", "2", "trailing" } };
    }

    [Theory]
    [MemberData(nameof(MalformedFrames))]
    public async Task MalformedOrTrailingFramesNeverReachTheService(object[] values)
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var metadata = new RecordingBranding();

        await new SaveBrandingItemEvent(metadata).Parse(client, HabbiconTestSupport.Incoming(values));

        Assert.Empty(metadata.Requests);
    }

    [Fact]
    public async Task TruncatedFrameFailsDecodingBeforeAnyServiceCall()
    {
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var metadata = new RecordingBranding();

        await Assert.ThrowsAnyAsync<Exception>(() => new SaveBrandingItemEvent(metadata).Parse(client, HabbiconTestSupport.Incoming(7, 4, "a", "1")));

        Assert.Empty(metadata.Requests);
    }

    private sealed class RecordingBranding : IRoomItemMetadataService
    {
        public List<BrandingRequest> Requests { get; } = [];
        public void SetBranding(GameClient session, BrandingRequest request) => Requests.Add(request);
        public void SetMannequinFigure(GameClient session, uint itemId) => throw new NotSupportedException();
        public void SetMannequinName(GameClient session, MannequinNameRequest request) => throw new NotSupportedException();
        public void SetToner(Room room, GameClient session, TonerSettingsRequest request) => throw new NotSupportedException();
    }
}
