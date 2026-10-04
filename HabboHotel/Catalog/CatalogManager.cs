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
    private Dictionary<int, CatalogDeal> _deals = new();
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

    public async Task Start() => await Init();

    public async Task Init()
    {
        _voucherManager.Init();
        _clothingManager.Init();
        var pagesById = new Dictionary<int, CatalogPage>();
        var botPresets = new Dictionary<uint, CatalogBot>();
        var itemsByPage = new Dictionary<int, Dictionary<int, CatalogItem>>();
        var dealsById = new Dictionary<int, CatalogDeal>();
        var promotionsById = new Dictionary<int, CatalogPromotion>();
        var offerIndex = new CatalogOfferIndex();
        var clubOffersById = new Dictionary<int, ClubOffer>();

        using var connection = _database.Connection();

        var items = await connection.QueryAsync<CatalogItem>("SELECT `id`,`item_id`,`catalog_name`,`cost_credits`,`cost_pixels`,`cost_diamonds`,`amount`,`page_id`,`limited_sells`,`limited_stack`,`offer_active` = '1' AS HaveOffer,`extradata`,`badge`,`offer_id`,`habbicon_id`,`club_level` AS `ClubLevel`,`preview_image` AS `PreviewImage`,`order_num` AS `OrderNum` FROM `catalog_items` ORDER BY `order_num`, `id`");
        foreach(CatalogItem item in items)
        {
            if (item.Amount <= 0)
                continue;

            ItemDefinition? definition = null;
            if (item.HabbiconId <= 0 && !_itemDataManager.Items.TryGetValue(item.ItemId, out definition))
            {
                _logger.LogError("Couldn't load Catalog Item " + item.ItemId + ", no furniture record found.");
                continue;
            }

            if (!itemsByPage.ContainsKey(item.PageId))
                itemsByPage[item.PageId] = new();

            item.Definition = definition;
            itemsByPage[item.PageId].Add(item.Id, item);
        }

        var deals = await connection.QueryAsync<CatalogDeal>("SELECT `id`, `items`, `name`, `room_id` FROM `catalog_deals`");
        foreach (CatalogDeal deal in deals)
        {
            if (dealsById.ContainsKey(deal.Id))
                continue;

            var itemDataList = new List<CatalogItem>();
            if (!string.IsNullOrWhiteSpace(deal.Items))
            {
                var splitItems = deal.Items.Split(';');
                foreach (var split in splitItems)
                {
                    var item = split.Split('*');
                    if (!uint.TryParse(item[0], out var itemId) || !int.TryParse(item[1], out var amount))
                        continue;

                    if (!_itemDataManager.Items.TryGetValue(itemId, out var data))
                        continue;

                    itemDataList.Add(new()
                    {
                        Id = 0,
                        ItemId = itemId,
                        Definition = data,
                        CatalogName = string.Empty,
                        PageId = 0,
                        CostCredits = 0,
                        CostPixels = 0,
                        CostDiamonds = 0,
                        Amount = amount,
                        LimitedEditionSells = 0,
                        LimitedEditionStack = 0,
                        HaveOffer = true,
                        ExtraData = "",
                        Badge = "",
                        OfferId = 0
                    });
                }
                deal.ItemDataList = itemDataList;
            }

            dealsById.Add(deal.Id, deal);
        }

        var pages = await connection.QueryAsync<CatalogPage>("SELECT `id`,`parent_id`,`caption`,`page_link` as `link`,`visible`,`enabled`,`required_permission` AS `RequiredPermission`,`required_club_level` AS `RequiredClubLevel`,`icon_image` as `icon`,`page_layout` as `layout`,`catalog_mode` AS `CatalogMode`,`page_strings_1`,`page_strings_2` FROM `catalog_pages` ORDER BY `order_num`, `id`");
        foreach (CatalogPage page in pages)
        {
            if (itemsByPage.ContainsKey(page.Id))
                page.Items = itemsByPage[page.Id];

            page.PageStringsList1 = !string.IsNullOrWhiteSpace(page.PageStrings1) ? page.PageStrings1!.Split("|").ToList() : new();
            page.PageStringsList2 = !string.IsNullOrWhiteSpace(page.PageStrings2) ? page.PageStrings2!.Split("|").ToList() : new();
            pagesById.Add(page.Id, page);
        }

        offerIndex.Build(pagesById.Values);

        var bots = await connection.QueryAsync<CatalogBot>("SELECT `id`,`name`,`figure`,`motto`,`gender`,`ai_type` FROM `catalog_bot_presets`");
        foreach (CatalogBot bot in bots)
        {
            botPresets.Add(bot.Id, bot);
        }

        var promotions = await connection.QueryAsync<CatalogPromotion>("SELECT `id`,`title`,`image`,`unknown`,`page_link`,`parent_id`,`position`,`item_type` AS `ItemType`,`offer_id` AS `OfferId`,`product_code` AS `ProductCode`,`expires_at` AS `ExpiresAt` FROM `catalog_promotions`");
        foreach(CatalogPromotion promotion in promotions)
        {
            if (promotionsById.ContainsKey(promotion.Id))
                continue;

            promotionsById.Add(promotion.Id, promotion);
        }

        var clubOffers = await connection.QueryAsync<ClubOffer>("SELECT `id`,`name`,`days`,`credits`,`points`,`points_type` AS `PointsType`,`giftable` AS `Giftable` FROM `catalog_club_offers` WHERE `enabled` = 1 ORDER BY `id`");
        foreach (var offer in clubOffers)
            clubOffersById.Add(offer.Id, offer);

        _pages = pagesById;
        _botPresets = botPresets;
        _deals = dealsById;
        _promotions = promotionsById;
        _offers = offerIndex;
        _clubOffers = clubOffersById;
        _petRaceManager.Init();
        _clothingManager.Init();
        _logger.LogInformation("Catalog Manager -> LOADED");
    }

    public bool TryGetBot(uint itemId, out CatalogBot bot) => _botPresets.TryGetValue(itemId, out bot);

    public bool TryGetPage(int pageId, out CatalogPage page) => _pages.TryGetValue(pageId, out page);

    public bool TryGetDeal(int dealId, out CatalogDeal deal) => _deals.TryGetValue(dealId, out deal);

    public bool TryGetOffer(int offerId, Habbo habbo, out CatalogPage page, out CatalogItem item) => _offers.TryGet(offerId, habbo, out page, out item);

    public ICollection<CatalogPage> Pages => _pages.Values;

    public ICollection<CatalogPromotion> Promotions => _promotions.Values;

    public ICollection<ClubOffer> ClubOffers => _clubOffers.Values;

    public bool TryGetClubOffer(int offerId, out ClubOffer offer) => _clubOffers.TryGetValue(offerId, out offer);

    public IMarketplaceManager Marketplace => _marketplace;

    public IPetRaceManager PetRaceManager => _petRaceManager;

    public IVoucherManager VoucherManager => _voucherManager;

    public IClothingManager ClothingManager => _clothingManager;
}