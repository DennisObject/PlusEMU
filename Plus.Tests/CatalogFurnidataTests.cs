using System.Text.Json.Nodes;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogFurnidataTests
{
    private static ItemDefinition Floor(string name) => new() { Id = (uint)name.GetHashCode() & 0xffff, ItemName = name, ProductType = "s" };

    private static CatalogOffer Offer(int id, params (ItemDefinition Definition, int Amount)[] products) => new()
    {
        Id = id,
        Products = products.Select(product => new CatalogProduct { Type = CatalogProductType.Furni, Definition = product.Definition, Amount = product.Amount }).ToList()
    };

    private static CatalogPage Page(int id, int parentId, params CatalogOffer[] offers) => new()
    {
        Id = id,
        ParentId = parentId,
        Enabled = true,
        Visible = true,
        Offers = offers.ToDictionary(offer => offer.Id)
    };

    private static JsonObject Furnidata(params string[] floorClassnames) => new()
    {
        ["roomitemtypes"] = new JsonObject
        {
            ["furnitype"] = new JsonArray(floorClassnames.Select((classname, index) => (JsonNode)new JsonObject
            {
                ["id"] = index + 1,
                ["classname"] = classname,
                ["name"] = classname,
                ["offerid"] = 99999,
                ["buyout"] = true,
                ["rentofferid"] = 5,
                ["rentbuyout"] = true,
                ["bc"] = true
            }).ToArray())
        },
        ["wallitemtypes"] = new JsonObject { ["furnitype"] = new JsonArray() }
    };

    private static JsonObject Entry(JsonObject root, string classname) =>
        root["roomitemtypes"]!["furnitype"]!.AsArray().OfType<JsonObject>().Single(entry => (string)entry["classname"]! == classname);

    [Fact]
    public void NamesTheOfferThatSellsTheFurniAloneBeforeMultiPacksAndBundles()
    {
        var chair = Floor("chair");
        var table = Floor("Table*2");
        var lamp = Floor("lamp");
        var root = Furnidata("chair", "table*2", "lamp", "unsold");

        CatalogFurnidata.Overlay(root, [
            Page(1, -1, Offer(30, (chair, 1), (table, 1)), Offer(20, (chair, 6)), Offer(25, (chair, 1))),
            Page(2, -1, Offer(10, (table, 1), (lamp, 1)), Offer(11, (table, 2)))
        ]);

        Assert.Equal(25, (int)Entry(root, "chair")["offerid"]!);
        Assert.True((bool)Entry(root, "chair")["buyout"]!);
        Assert.Equal(11, (int)Entry(root, "table*2")["offerid"]!);
        Assert.Equal(10, (int)Entry(root, "lamp")["offerid"]!);
        Assert.False((bool)Entry(root, "lamp")["buyout"]!);
        Assert.Equal(-1, (int)Entry(root, "unsold")["offerid"]!);
        Assert.False((bool)Entry(root, "unsold")["buyout"]!);

        foreach (var entry in root["roomitemtypes"]!["furnitype"]!.AsArray().OfType<JsonObject>()) {
            Assert.Equal(-1, (int)entry["rentofferid"]!);
            Assert.False((bool)entry["rentbuyout"]!);
            Assert.False((bool)entry["bc"]!);
        }
    }

    [Fact]
    public void SkipsOffersPlayersCannotFindAndPrefersOpenPages()
    {
        var chair = Floor("chair");
        var sofa = Floor("sofa");
        var disabledOffer = Offer(1, (chair, 1));
        disabledOffer.Enabled = false;
        var staff = Page(10, -1);
        staff.RequiredPermission = "catalog.staff";
        var disabledPage = Page(11, -1, Offer(2, (chair, 1)));
        disabledPage.Enabled = false;
        var hidden = Page(12, -1, Offer(4, (sofa, 1)));
        hidden.Visible = false;
        var root = Furnidata("chair", "sofa");

        CatalogFurnidata.Overlay(root, [
            staff,
            Page(13, 10, Offer(3, (chair, 1))),
            disabledPage,
            Page(14, 999, Offer(5, (chair, 1))),
            Page(15, -1, disabledOffer, Offer(6, (chair, 1))),
            hidden,
            Page(16, -1, Offer(7, (sofa, 1)))
        ]);

        Assert.Equal(6, (int)Entry(root, "chair")["offerid"]!);
        Assert.Equal(7, (int)Entry(root, "sofa")["offerid"]!);
    }

    [Fact]
    public void GeneratesEveryEntryInHabbosFieldOrderWithTheCatalogsOffers()
    {
        var chair = new FurnidataEntry
        {
            Id = 13,
            Classname = "chair",
            Revision = 61856,
            Category = "chair",
            DefaultDir = 2,
            XDim = 1,
            YDim = 2,
            PartColors = "#ffffff,#0",
            Name = "Chair",
            Description = "Sit",
            AdUrl = null,
            CustomParams = "",
            SpecialType = 1,
            CanSitOn = true,
            FurniLine = "iced",
            Environment = null,
            Rare = true,
            Height = 0.9,
            Tradeable = false
        };
        var poster = new FurnidataEntry { Id = 4001, Classname = "poster", IsWall = true, Name = "Poster", Category = "unknown", PartColors = "", CanPutStuffOn = true };

        var file = CatalogFurnidata.Generate([chair, poster], [Page(1, -1, Offer(7, (Floor("chair"), 1)))]);
        var root = JsonNode.Parse(file.Content)!.AsObject();

        Assert.Equal("""
            {"id":13,"classname":"chair","revision":61856,"category":"chair","defaultdir":2,"xdim":1,"ydim":2,"partcolors":{"color":["#ffffff","#0"]},"name":"Chair","description":"Sit","adurl":null,"offerid":7,"buyout":true,"rentofferid":-1,"rentbuyout":false,"bc":false,"excludeddynamic":false,"customparams":"","specialtype":1,"canstandon":false,"cansiton":true,"canlayon":false,"height":0.9,"furniline":"iced","environment":null,"rare":true,"tradeable":false}
            """, Entry(root, "chair").ToJsonString());
        // Wall entries have no floor fields.
        Assert.Equal("""
            {"id":4001,"classname":"poster","revision":0,"category":"unknown","name":"Poster","description":null,"adurl":null,"offerid":-1,"buyout":false,"rentofferid":-1,"rentbuyout":false,"bc":false,"excludeddynamic":false,"customparams":null,"specialtype":1,"furniline":null,"environment":null,"rare":false}
            """, root["wallitemtypes"]!["furnitype"]![0]!.ToJsonString());
        Assert.Matches("^\"[0-9a-f]{40}\"$", file.ETag);
    }


    [Fact]
    public void DefinitionsCompareWithoutCatalogFieldsOrFieldOrder()
    {
        var entry = new FurnidataEntry { Id = 1, Classname = "chair", Name = "Chair" };
        var stored = JsonNode.Parse("""{"name":"Chair","offerid":5,"buyout":true}""")!.AsObject();

        foreach (var (key, value) in entry.ToJson(catalog: false).Where(field => field.Key != "name")) {
            stored[key] = value?.DeepClone();
        }

        Assert.True(FurnidataEntry.SameDefinition(entry.ToJson(catalog: true), stored));
        stored["name"] = "Other";
        Assert.False(FurnidataEntry.SameDefinition(entry.ToJson(catalog: true), stored));
    }
}
