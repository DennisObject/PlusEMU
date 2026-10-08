using System.Data;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Database;
using Plus.HabboHotel.Catalog.Clothing;
using Plus.HabboHotel.Catalog.Marketplace;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.Catalog.Vouchers;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Catalog;

public class CatalogManager : ICatalogManager, IStartable
{
    private readonly ILogger<CatalogManager> _logger;
    // Init builds new collections and swaps them in, so a reload never shows readers a half-loaded catalog.
    private Dictionary<uint, CatalogBot> _botPresets = new();
    private Dictionary<int, CatalogPage> _pages = new();
    private Dictionary<int, CatalogPromotion> _promotions = new();
    private CatalogOfferIndex _offers = new();
    private Dictionary<int, ClubOffer> _clubOffers = new();

    private readonly IClothingManager _clothingManager;
    private readonly IDatabase _database;
    private readonly IMarketplaceManager _marketplace;
    private readonly IPetRaceManager _petRaceManager;
    private readonly IVoucherManager _voucherManager;
    private readonly IItemDataManager _itemDataManager;

    public CatalogManager(IMarketplaceManager marketplace, IPetRaceManager petRaceManager, IVoucherManager voucherManager, IClothingManager clothingManager, IDatabase database, ILogger<CatalogManager> logger, IItemDataManager itemDataManager)
    {
        _marketplace = marketplace;
        _petRaceManager = petRaceManager;
        _voucherManager = voucherManager;
        _clothingManager = clothingManager;
        _itemDataManager = itemDataManager;
        _database = database;
        _logger = logger;
    }

    public int StartOrder => 30;

    public Task Start() => Load();

    public async Task Init()
    {
        _voucherManager.Init();
        _clothingManager.Init();
        _petRaceManager.Init();
        await Load();
    }

    private async Task Load()
    {
        var pagesById = new Dictionary<int, CatalogPage>();
        var botPresets = new Dictionary<uint, CatalogBot>();
        var promotionsById = new Dictionary<int, CatalogPromotion>();
        var offerIndex = new CatalogOfferIndex();
        var clubOffersById = new Dictionary<int, ClubOffer>();

        using var connection = _database.Connection();

        var offers = await LoadOffers(connection);
        var pages = await connection.QueryAsync<CatalogPage>("SELECT `id`, COALESCE(`parent_id`, -1) AS `ParentId`, `caption`, COALESCE(`link`, '') AS `Link`, `visible`, `enabled`, " +
            "`required_permission` AS `RequiredPermission`, `required_club_level` AS `RequiredClubLevel`, `icon`, `layout` FROM `catalog_pages` ORDER BY `position`, `id`");

        foreach (CatalogPage page in pages) {
            pagesById.Add(page.Id, page);
        }

        foreach (var (pageId, slot, image) in await connection.QueryAsync<(int, int, string)>("SELECT `page_id`, `slot`, `image` FROM `catalog_page_images`")) {
            if (pagesById.TryGetValue(pageId, out var page)) {
                SetSlot(page.Images, slot, image);
            }
        }

        foreach (var (pageId, slot, text) in await connection.QueryAsync<(int, int, string)>("SELECT `page_id`, `slot`, `text` FROM `catalog_page_texts`")) {
            if (pagesById.TryGetValue(pageId, out var page)) {
                SetSlot(page.Texts, slot, text);
            }
        }

        foreach (var (pageId, offerId) in await connection.QueryAsync<(int, int)>("SELECT `page_id`, `offer_id` FROM `catalog_page_offers` ORDER BY `page_id`, `position`, `offer_id`")) {
            if (pagesById.TryGetValue(pageId, out var page) && offers.TryGetValue(offerId, out var offer)) {
                page.Offers.Add(offerId, offer);
            }
        }

        offerIndex.Build(pagesById.Values);

        var bots = await connection.QueryAsync<CatalogBot>("SELECT `id`,`name`,`figure`,`motto`,`gender`,`ai_type` FROM `catalog_bot_presets`");

        foreach (CatalogBot bot in bots) {
            botPresets.Add(bot.Id, bot);
        }

        var promotions = await connection.QueryAsync<CatalogPromotion>("SELECT `id`,`title`,`image`,`unknown`,`page_link`,`parent_id`,`position`,`item_type` AS `ItemType`,`offer_id` AS `OfferId`,`product_code` AS `ProductCode`,`expires_at` AS `ExpiresAt` FROM `catalog_promotions`");

        foreach (CatalogPromotion promotion in promotions) {
            if (promotionsById.ContainsKey(promotion.Id)) {
                continue;
            }

            promotionsById.Add(promotion.Id, promotion);
        }

        var clubOffers = await connection.QueryAsync<ClubOffer>("SELECT `id`,`name`,`days`,`credits`,`points`,`points_type` AS `PointsType`,`giftable` AS `Giftable` FROM `catalog_club_offers` WHERE `enabled` = 1 ORDER BY `id`");

        foreach (var offer in clubOffers) {
            clubOffersById.Add(offer.Id, offer);
        }

        _pages = pagesById;
        _botPresets = botPresets;
        _promotions = promotionsById;
        _offers = offerIndex;
        _clubOffers = clubOffersById;
        Revision++;
        _logger.LogInformation("Catalog Manager -> LOADED");
    }

    // Offers with their products. An offer that cannot be sold as stored is left out and logged.
    private async Task<Dictionary<int, CatalogOffer>> LoadOffers(IDbConnection connection)
    {
        var products = (await connection.QueryAsync<CatalogProductRow>("SELECT `offer_id` AS `OfferId`, `product_type` AS `ProductType`, " +
                "`furniture_id` AS `FurnitureId`, `effect_id` AS `EffectId`, `badge_code` AS `BadgeCode`, `bot_preset_id` AS `BotPresetId`, " +
                "`pet_type` AS `PetType`, `habbicon_id` AS `HabbiconId`, `amount`, `extra_param` AS `ExtraParam` FROM `catalog_offer_products` ORDER BY `offer_id`, `position`"))
            .ToLookup(row => row.OfferId);
        var rows = await connection.QueryAsync<CatalogOfferRow>("SELECT o.`id`, o.`localization_key` AS `LocalizationKey`, o.`cost_credits` AS `CostCredits`, " +
            "o.`cost_points` AS `CostPoints`, o.`points_type` AS `PointsType`, o.`club_level` AS `ClubLevel`, o.`bulk_purchase` AS `BulkPurchase`, o.`enabled`, " +
            "o.`preview_image` AS `PreviewImage`, COALESCE(l.`stack`, 0) AS `LimitedStack`, COALESCE(l.`sold`, 0) AS `LimitedSold` " +
            "FROM `catalog_offers` o LEFT JOIN `catalog_offer_limited` l ON l.`offer_id` = o.`id`");
        var offers = new Dictionary<int, CatalogOffer>();

        foreach (var row in rows) {
            var offerProducts = new List<CatalogProduct>();

            foreach (var product in products[row.Id]) {
                if (ToProduct(product) is { } loaded) {
                    offerProducts.Add(loaded);
                }
                else {
                    offerProducts = null;
                    break;
                }
            }

            if (offerProducts is not { Count: > 0 } || row.PointsType is not (0 or 5)) {
                _logger.LogError("Skipped catalog offer #{Id}: it has no products, a product that cannot be loaded or an unknown points type", row.Id);
                continue;
            }

            offers.Add(row.Id, new CatalogOffer
            {
                Id = row.Id,
                LocalizationKey = row.LocalizationKey,
                CostCredits = (int)row.CostCredits,
                CostPixels = row.PointsType == 0 ? (int)row.CostPoints : 0,
                CostDiamonds = row.PointsType == 5 ? (int)row.CostPoints : 0,
                ClubLevel = row.ClubLevel,
                BulkPurchase = row.BulkPurchase,
                Enabled = row.Enabled,
                PreviewImage = row.PreviewImage,
                LimitedStack = row.LimitedStack,
                LimitedSells = row.LimitedSold,
                Products = offerProducts
            });
        }

        return offers;
    }

    private CatalogProduct? ToProduct(CatalogProductRow row)
    {
        switch (row.ProductType) {
            case "furni":
                return row.FurnitureId is { } furnitureId && _itemDataManager.Items.TryGetValue(furnitureId, out var definition)
                    ? new() { Type = CatalogProductType.Furni, Definition = definition, Amount = row.Amount, ExtraParam = row.ExtraParam }
                    : null;
            case "effect":
                return new() { Type = CatalogProductType.Effect, EffectId = row.EffectId ?? 0, Amount = row.Amount };
            case "badge":
                return new() { Type = CatalogProductType.Badge, BadgeCode = row.BadgeCode ?? string.Empty };
            case "bot":
                return new() { Type = CatalogProductType.Bot, BotPresetId = row.BotPresetId ?? 0, Amount = row.Amount };
            case "pet":
                return new() { Type = CatalogProductType.Pet, PetType = row.PetType ?? 0, Amount = row.Amount };
            case "habbicon":
                return new() { Type = CatalogProductType.Habbicon, HabbiconId = row.HabbiconId ?? 0 };
            default:
                return null;
        }
    }

    private static void SetSlot(List<string> values, int slot, string value)
    {
        while (values.Count <= slot) {
            values.Add(string.Empty);
        }

        values[slot] = value;
    }

    private sealed class CatalogOfferRow
    {
        public int Id { get; set; }
        public string LocalizationKey { get; set; } = string.Empty;
        public uint CostCredits { get; set; }
        public uint CostPoints { get; set; }
        public uint PointsType { get; set; }
        public int ClubLevel { get; set; }
        public bool BulkPurchase { get; set; }
        public bool Enabled { get; set; }
        public string PreviewImage { get; set; } = string.Empty;
        public uint LimitedStack { get; set; }
        public uint LimitedSold { get; set; }
    }

    private sealed class CatalogProductRow
    {
        public int OfferId { get; set; }
        public string ProductType { get; set; } = string.Empty;
        public uint? FurnitureId { get; set; }
        public int? EffectId { get; set; }
        public string? BadgeCode { get; set; }
        public int? BotPresetId { get; set; }
        public int? PetType { get; set; }
        public int? HabbiconId { get; set; }
        public int Amount { get; set; }
        public string ExtraParam { get; set; } = string.Empty;
    }

    public bool TryGetBot(uint itemId, [NotNullWhen(true)] out CatalogBot? bot) => _botPresets.TryGetValue(itemId, out bot);

    public bool TryGetPage(int pageId, [NotNullWhen(true)] out CatalogPage? page) => _pages.TryGetValue(pageId, out page);

    public bool TryGetOffer(int offerId, Habbo habbo, [NotNullWhen(true)] out CatalogPage? page, [NotNullWhen(true)] out CatalogOffer? offer) => _offers.TryGet(offerId, habbo, out page, out offer);

    public ICollection<CatalogPage> Pages => _pages.Values;

    public int Revision { get; private set; }

    public ICollection<CatalogPromotion> Promotions => _promotions.Values;

    public ICollection<ClubOffer> ClubOffers => _clubOffers.Values;

    public bool TryGetClubOffer(int offerId, [NotNullWhen(true)] out ClubOffer? offer) => _clubOffers.TryGetValue(offerId, out offer);

    public IMarketplaceManager Marketplace => _marketplace;

    public IPetRaceManager PetRaceManager => _petRaceManager;

    public IVoucherManager VoucherManager => _voucherManager;

    public IClothingManager ClothingManager => _clothingManager;
}
