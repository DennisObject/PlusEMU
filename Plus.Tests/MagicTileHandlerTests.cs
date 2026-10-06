using Plus.Communication.Packets.Incoming.Rooms.Furni;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class MagicTileHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateFullyDecodesBothHeightFramesBeforeDelegating(bool withMultiWalk)
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var service = new RecordingTiles();
        var packet = withMultiWalk ? HabbiconTestSupport.Incoming(-2, 175, true) : HabbiconTestSupport.Incoming(-2, 175);
        await new UpdateMagicTileEvent(service).Parse(client, packet);
        Assert.Equal((client, uint.MaxValue - 1, 175, withMultiWalk ? (bool?)true : null), service.Updated);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
    }

    [Fact]
    public async Task AdjacentFullyDecodesBeforeDelegating()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 1 });
        var service = new RecordingTiles();
        var packet = HabbiconTestSupport.Incoming(7, false);
        await new UpdateMagicTileAdjacentEvent(service).Parse(client, packet);
        Assert.Equal((client, 7u, false), service.Adjacent);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
    }

    private sealed class RecordingTiles : IMagicTileService
    {
        public (GameClient, uint, int, bool?)? Updated;
        public (GameClient, uint, bool)? Adjacent;
        public void Update(GameClient session, uint itemId, int requestedHeight, bool? multiWalk) =>
            Updated = (session, itemId, requestedHeight, multiWalk);
        public void UpdateAdjacent(GameClient session, uint itemId, bool moveDown) => Adjacent = (session, itemId, moveDown);
    }
}
