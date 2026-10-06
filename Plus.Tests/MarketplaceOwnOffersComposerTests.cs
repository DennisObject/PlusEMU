using Xunit;
using Plus.Communication.Packets.Outgoing.Marketplace;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.GameClients;
using Xunit;

namespace Plus.Tests;

public class MarketplaceOwnOffersComposerTests
{
    [Fact]
    public void SerializesPreparedOffersAndSnapshotsMutableInput()
    {
        var offers = new[]
        {
            new MarketplaceOwnOffer(10, 1, 100, 0, 0, 500, 60),
            new MarketplaceOwnOffer(11, 2, 101, 0, 0, 600, -4),
            new MarketplaceOwnOffer(12, 3, 102, 7, 20, 700, 0)
        };
        var composer = new MarketPlaceOwnOffersComposer(new MarketplaceOwnOffers(900, offers));
        offers[0] = new MarketplaceOwnOffer(99, 9, 999, 9, 9, 9, 9);

        var first = new RecordingPacket();
        var second = new RecordingPacket();
        composer.Compose(first);
        composer.Compose(second);

        Assert.Equal(first.Writes, second.Writes);
        Assert.Equal(new object[]
        {
            900, 3,
            10, 1, 1, 100, 256, "", 0, 0, 500, 60, 100,
            11, 2, 1, 101, 256, "", 0, 0, 600, -4, 101,
            12, 3, 1, 102, 256, "", 7, 20, 700, 0, 102
        }, first.Writes);
    }

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value);
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
