using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Editor;
using Xunit;

namespace Plus.Tests;

public sealed class CatalogFurnidataTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("plus-catalog-furnidata-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

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
    public void RebuildsWhenTheFileOrCatalogChanges()
    {
        var path = Path.Combine(_directory, "FurnitureData.json");
        File.WriteAllText(path, Furnidata("chair").ToJsonString());
        var store = new FurnidataStore(Options.Create(new FurniEditorConfiguration { FurnidataPath = path }));
        var pages = new List<CatalogPage> { Page(1, -1, Offer(5, (Floor("chair"), 1))) };
        var revision = 1;
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) => method switch
        {
            "get_Revision" => revision,
            "get_Pages" => pages,
            _ => throw new NotSupportedException(method)
        });
        var furnidata = new CatalogFurnidata(store, catalog);

        var first = furnidata.Current()!;
        Assert.Same(first, furnidata.Current());
        Assert.Equal(5, (int)Entry(JsonNode.Parse(first.Content)!.AsObject(), "chair")["offerid"]!);
        Assert.Matches("^\"[0-9a-f]{40}\"$", first.ETag);

        pages = [Page(1, -1, Offer(8, (Floor("chair"), 1)))];
        revision++;
        var second = furnidata.Current()!;
        Assert.Equal(8, (int)Entry(JsonNode.Parse(second.Content)!.AsObject(), "chair")["offerid"]!);
        Assert.NotEqual(first.ETag, second.ETag);

        store.Edit(new FurnidataTarget("chair", 1, false), entry => entry["name"] = "Renamed");
        var third = furnidata.Current()!;
        Assert.Equal("Renamed", (string)Entry(JsonNode.Parse(third.Content)!.AsObject(), "chair")["name"]!);
        Assert.Equal(8, (int)Entry(JsonNode.Parse(third.Content)!.AsObject(), "chair")["offerid"]!);
    }

    [Fact]
    public void ServesNothingWithoutAFurnidataFile()
    {
        var store = new FurnidataStore(Options.Create(new FurniEditorConfiguration()));
        var catalog = CatalogSnapshotTestSupport.Proxy<ICatalogManager>((method, _) => throw new NotSupportedException(method));

        Assert.Null(new CatalogFurnidata(store, catalog).Current());
    }
}
