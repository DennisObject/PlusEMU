using Plus.Core;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.HabboHotel.GameClients;
using System.Diagnostics.CodeAnalysis;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Navigator.SavedSearches;

namespace Plus.HabboHotel.Navigator;

public sealed class NavigatorManager : INavigatorManager, IStartable
{
    private readonly IDatabase _database;
    private readonly IRoomDataLoader _rooms;
    private readonly ILogger<NavigatorManager> _logger;

    private readonly Dictionary<uint, FeaturedRoom> _featuredRooms;
    private readonly Dictionary<int, SearchResultList> _searchResultLists;
    private readonly Dictionary<int, TopLevelItem> _topLevelItems;

    public NavigatorManager(IDatabase database, ILogger<NavigatorManager> logger, IRoomDataLoader rooms)
    {
        _database = database;
        _rooms = rooms;
        _logger = logger;
        _topLevelItems = new();
        _searchResultLists = new();

        //Does this need to be dynamic?
        _topLevelItems.Add(1, new(1, "official_view", "", ""));
        _topLevelItems.Add(2, new(2, "hotel_view", "", ""));
        _topLevelItems.Add(3, new(3, "roomads_view", "", ""));
        _topLevelItems.Add(4, new(4, "myworld_view", "", ""));
        _featuredRooms = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var categories = await connection.QueryAsync<CategoryRow>("SELECT id, category, category_identifier AS CategoryIdentifier, public_name AS PublicName, COALESCE(required_permission, '') AS RequiredPermission, view_mode AS ViewMode, category_type AS CategoryType, search_allowance AS SearchAllowance, order_id AS OrderId FROM navigator_categories WHERE enabled = TRUE ORDER BY id");
        var publics = await connection.QueryAsync<FeaturedRoom>("SELECT room_id AS RoomId, caption, description, image_url AS Images FROM navigator_publics WHERE enabled = TRUE ORDER BY order_num");
        _searchResultLists.Clear();
        _featuredRooms.Clear();

        foreach (var category in categories) {
            _searchResultLists.TryAdd(category.Id, new(category.Id, category.Category, category.CategoryIdentifier, category.PublicName, true, -1, category.RequiredPermission, NavigatorViewModeUtility.GetViewModeByString(category.ViewMode), category.CategoryType, category.SearchAllowance, category.OrderId));
        }

        foreach (var featured in publics) {
            _featuredRooms.TryAdd((uint)featured.RoomId, featured);
        }

        _logger.LogInformation("Navigator -> LOADED");
    }

    private sealed class CategoryRow
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string CategoryIdentifier { get; set; } = string.Empty;
        public string PublicName { get; set; } = string.Empty;
        public string RequiredPermission { get; set; } = string.Empty;
        public string ViewMode { get; set; } = string.Empty;
        public string CategoryType { get; set; } = string.Empty;
        public string SearchAllowance { get; set; } = string.Empty;
        public int OrderId { get; set; }
    }

    public List<SearchResultList> GetCategoriessForSearch(string category) => _searchResultLists.Where(cat => cat.Value.Category == category).OrderBy(cat => cat.Value.OrderId).Select(cat => cat.Value).ToList();

    public IReadOnlyCollection<SearchResultList> GetResultByIdentifier(string category) => _searchResultLists.Where(cat => cat.Value.CategoryIdentifier == category).OrderBy(cat => cat.Value.OrderId).Select(cat => cat.Value).ToList();

    public IReadOnlyCollection<SearchResultList> FlatCategories => _searchResultLists.Where(cat => cat.Value.CategoryType == NavigatorCategoryType.Category).OrderBy(cat => cat.Value.OrderId).Select(cat => cat.Value).ToList();

    public IReadOnlyCollection<SearchResultList> EventCategories => _searchResultLists.Where(cat => cat.Value.CategoryType == NavigatorCategoryType.PromotionCategory).OrderBy(cat => cat.Value.OrderId).Select(cat => cat.Value).ToList();

    public IReadOnlyCollection<TopLevelItem> TopLevelItems => _topLevelItems.Values;

    public IReadOnlyCollection<SearchResultList> SearchResultLists => _searchResultLists.Values;

    public bool TryGetTopLevelItem(int id, [NotNullWhen(true)] out TopLevelItem? topLevelItem) => _topLevelItems.TryGetValue(id, out topLevelItem);

    public bool TryGetSearchResultList(int id, [NotNullWhen(true)] out SearchResultList? searchResultList) => _searchResultLists.TryGetValue(id, out searchResultList);

    public bool TryGetFeaturedRoom(uint roomId, [NotNullWhen(true)] out FeaturedRoom? publicRoom) => _featuredRooms.TryGetValue(roomId, out publicRoom);

    public IReadOnlyCollection<FeaturedRoom> FeaturedRooms => _featuredRooms.Values;

    public async Task<Dictionary<int, SavedSearch>> LoadUserNavigatorPreferences(int userId)
    {
        using var connection = _database.Connection();

        return (await connection.QueryAsync<SavedSearch>("SELECT `id`,`filter`,`search_code` as search FROM `user_saved_searches` WHERE `user_id` = @userId", new { userId })).ToDictionary(search => search.Id);
    }

    public Task SaveHomeRoom(GameClient session, uint roomId)
    {
        var habbo = session.GetHabbo();
        var exists = _rooms.TryGetData(roomId, out _);

        lock (habbo.WalletSync) {
            if (habbo.WalletClosed) {
                return Task.CompletedTask;
            }

            if (exists) {
                using var connection = _database.Connection();
                var updated = connection.Execute(
                    "UPDATE users_settings SET home_room = @roomId WHERE user_id = @userId LIMIT 1",
                    new { roomId, userId = habbo.Id });

                if (updated != 1) {
                    throw new DBConcurrencyException($"Settings for user {habbo.Id} no longer exist.");
                }

                habbo.HomeRoom = roomId;
            }
        }

        session.Send(new NavigatorSettingsComposer(roomId));

        return Task.CompletedTask;
    }
}
