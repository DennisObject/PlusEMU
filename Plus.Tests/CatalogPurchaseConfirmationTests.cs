using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogPurchaseConfirmationTests
{
    [Theory]
    [InlineData("s")]
    [InlineData("b")]
    public void ConfirmationFieldsRetainLegacyShapeAndFreezeOfferAndDefinition(string type)
    {
        var definition = new ItemDefinition { Id = 70, ItemName = "product", ProductType = type, SpriteId = 99 };
        var product = type == "b"
            ? new CatalogProduct { Type = CatalogProductType.Badge, BadgeCode = "product" }
            : new CatalogProduct { Type = CatalogProductType.Furni, Definition = definition };
        var item = new CatalogOffer { Id = 7, LocalizationKey = "offer", CostCredits = 10, CostPoints = 20, PointsType = 5, Products = [product] };
        var composer = new PurchaseOKComposer(CatalogPurchaseConfirmation.Capture(item));
        var before = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(before);
        var expected = new List<object> { 7, "product", false, 10, 20, 5, true, 1, type };
        expected.AddRange(type == "b" ? new object[] { "product", 0, false } : new object[] { 99, "", 1, 0, "", 1 });
        Assert.Equal(expected, before.Writes);
        item.CostCredits = 100;
        item.CostPoints = 100;
        item.PointsType = 0;
        item.Id = 8;
        definition.Id = 71;
        definition.ItemName = "changed";
        definition.ProductType = "changed";
        definition.SpriteId = 100;
        var after = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(after);
        Assert.Equal(before.Writes, after.Writes);
    }

    [Fact]
    public void EmptyConfirmationRetainsItsFallbackWireFields()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        new PurchaseOKComposer().Compose(packet);
        Assert.Equal(new object[] { 0, "", false, 0, 0, 0, true, 1, "s", 0, "", 1, 0, "", 1 }, packet.Writes);
    }
}
