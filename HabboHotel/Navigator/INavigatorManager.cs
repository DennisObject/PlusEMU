using System.Diagnostics.CodeAnalysis;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Navigator.SavedSearches;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorManager
{
    void Init();
    List<SearchResultList> GetCategoriessForSearch(string category);
    IReadOnlyCollection<SearchResultList> GetResultByIdentifier(string category);
    IReadOnlyCollection<SearchResultList> FlatCategories { get; }
    IReadOnlyCollection<SearchResultList> EventCategories { get; }
    IReadOnlyCollection<TopLevelItem> TopLevelItems { get; }
    IReadOnlyCollection<SearchResultList> SearchResultLists { get; }
    bool TryGetTopLevelItem(int id, [NotNullWhen(true)] out TopLevelItem? topLevelItem);
    bool TryGetSearchResultList(int id, [NotNullWhen(true)] out SearchResultList? searchResultList);
    bool TryGetFeaturedRoom(uint roomId, [NotNullWhen(true)] out FeaturedRoom? publicRoom);
    IReadOnlyCollection<FeaturedRoom> FeaturedRooms { get; }
    Task<Dictionary<int, SavedSearch>> LoadUserNavigatorPreferences(int habboId);
    Task SaveHomeRoom(GameClient session, uint roomId);
}
