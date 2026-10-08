using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Plus.HabboHotel.Items.Editor;

namespace Plus.HabboHotel.Catalog;

// The generated FurnitureData.json and its entity tag (a quoted SHA-1 of the content).
public sealed record CatalogFurnidataFile(byte[] Content, string ETag);

public interface ICatalogFurnidata
{
    // Null when no furnidata file is configured.
    CatalogFurnidataFile? Current();
}

// FurnitureData.json as clients load it: the furni editor's file with each entry's purchase fields taken from the
// loaded catalog, the way Habbo generates furnidata from its catalog. The infostand buy button and catalog search
// open the entry's offerid, so it always names an offer the catalog sells. Rebuilt when the file or catalog changes.
public sealed class CatalogFurnidata(IFurnidataStore store, ICatalogManager catalog) : ICatalogFurnidata
{
    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const int MaximumPageDepth = 20;

    private readonly object _sync = new();
    private (FurnidataSource Source, int Revision, CatalogFurnidataFile File)? _cached;

    public CatalogFurnidataFile? Current()
    {
        if (store.Source() is not { } source) {
            return null;
        }

        int revision = catalog.Revision;

        lock (_sync) {
            if (_cached is { } cached && cached.Source == source && cached.Revision == revision) {
                return cached.File;
            }

            var root = JsonNode.Parse(File.ReadAllBytes(source.Path)) as JsonObject ?? throw new FurnidataException("Furnidata is not a JSON object");
            Overlay(root, catalog.Pages);
            var content = JsonSerializer.SerializeToUtf8Bytes(root, Compact);
            var file = new CatalogFurnidataFile(content, $"\"{Convert.ToHexStringLower(SHA1.HashData(content))}\"");
            _cached = (source, revision, file);

            return file;
        }
    }

    // Sets offerid to the offer that sells the entry's furni (-1 when none does) and buyout to whether that offer
    // sells only this furni. Rent and Builders Club offers do not exist here.
    public static void Overlay(JsonObject root, IEnumerable<CatalogPage> pages)
    {
        var offers = OffersByFurni(pages);

        foreach (var (section, isWall) in new[] { ("roomitemtypes", false), ("wallitemtypes", true) }) {
            if (root[section]?["furnitype"] is not JsonArray types) {
                continue;
            }

            foreach (var entry in types.OfType<JsonObject>()) {
                var classname = entry["classname"] is JsonValue value && value.TryGetValue<string>(out var text) ? text.ToLowerInvariant() : "";
                var offer = offers.GetValueOrDefault((classname, isWall));
                entry["offerid"] = offer?.Id ?? -1;
                entry["buyout"] = offer is { IsBundle: false };
                entry["rentofferid"] = -1;
                entry["rentbuyout"] = false;
                entry["bc"] = false;
            }
        }
    }

    // Per furni, the offer furnidata names, from pages a player without permissions finds in the catalog index:
    // one selling the furni alone and singly, then a multi-pack, then a bundle; an offer on a visible page with no
    // club requirement before one that is not; then the lowest id.
    private static Dictionary<(string Classname, bool IsWall), CatalogOffer> OffersByFurni(IEnumerable<CatalogPage> pages)
    {
        var byId = pages.ToDictionary(page => page.Id);
        var best = new Dictionary<(string, bool), (CatalogOffer Offer, (int, int, int) Rank)>();

        foreach (var page in byId.Values) {
            if (!page.Enabled || Placement(page, byId) is not { } open) {
                continue;
            }

            foreach (var offer in page.Offers.Values.Where(offer => offer.Enabled)) {
                var rank = (offer.IsBundle ? 2 : offer.Amount == 1 ? 0 : 1, open ? 0 : 1, offer.Id);

                foreach (var product in offer.Products.Where(product => product.Type == CatalogProductType.Furni)) {
                    var key = (product.Definition!.ItemName.ToLowerInvariant(), product.Definition.ProductType == "i");

                    if (!best.TryGetValue(key, out var current) || rank.CompareTo(current.Rank) < 0) {
                        best[key] = (offer, rank);
                    }
                }
            }
        }

        return best.ToDictionary(pair => pair.Key, pair => pair.Value.Offer);
    }

    // Null when the page is not in the index for a player without permissions; otherwise whether it and every page
    // above it are visible with no club requirement.
    private static bool? Placement(CatalogPage page, Dictionary<int, CatalogPage> byId)
    {
        bool open = true;

        for (int depth = 0; depth < MaximumPageDepth; depth++) {
            if (!string.IsNullOrEmpty(page.RequiredPermission)) {
                return null;
            }

            open &= page.Visible && page.RequiredClubLevel == 0;

            if (page.ParentId == -1) {
                return open;
            }

            if (!byId.TryGetValue(page.ParentId, out page!)) {
                return null;
            }
        }

        return null;
    }
}
