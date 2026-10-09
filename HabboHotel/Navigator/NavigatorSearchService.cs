using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Navigator;

public sealed record NavigatorResultBlock(string CategoryIdentifier, string PublicName, int Action, int ViewMode, ImmutableArray<RoomWireData> Rooms);
public sealed record NavigatorSearchSnapshot(string Category, string Query, ImmutableArray<NavigatorResultBlock> Results);

public interface INavigatorSearchService
{
    NavigatorSearchSnapshot Search(GameClient session, string category, string query);
}

public sealed class NavigatorSearchService(INavigatorManager navigator, INavigatorSearchStore store, IRoomManager rooms, IGroupManager groups,
    IRoomDataLoader roomData) : INavigatorSearchService
{
    public NavigatorSearchSnapshot Search(GameClient session, string category, string query)
    {
        IReadOnlyCollection<SearchResultList> categories;
        var goBack = 1;
        var limit = 12;

        if (!string.IsNullOrEmpty(query)) {
            categories = navigator.TryGetSearchResultList(0, out var result) ? [result] : [];
        }
        else {
            categories = navigator.GetCategoriessForSearch(category);

            if (categories.Count == 0) {
                categories = navigator.GetResultByIdentifier(category);

                if (categories.Count > 0) {
                    goBack = 2;
                    limit = 100;
                }
            }
        }

        return new(category, query, categories.Select(item => PrepareBlock(session, item, query, goBack, limit)).ToImmutableArray());
    }

    private NavigatorResultBlock PrepareBlock(GameClient session, SearchResultList result, string query, int goBack, int limit)
    {
        IEnumerable<RoomData> selected = result.CategoryType switch
        {
            NavigatorCategoryType.Query => Query(query),
            NavigatorCategoryType.Popular => rooms.GetPopularRooms(-1, limit).Select(room => room.Data),
            NavigatorCategoryType.Recommended => rooms.GetRecommendedRooms(limit).Select(room => room.Data),
            NavigatorCategoryType.Category => rooms.GetRoomsByCategory(result.Id, limit).Select(room => room.Data),
            NavigatorCategoryType.MyRooms => roomData.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id).OrderByDescending(room => room.UsersNow),
            NavigatorCategoryType.MyFavourites => Resolve(session.GetHabbo().FavoriteRooms.ToArray().OfType<uint>()),
            NavigatorCategoryType.MyGroups => Resolve(groups.GetGroupsForUser(session.GetHabbo().Id).Select(group => group.RoomId)).Take(limit),
            NavigatorCategoryType.MyFriendsRooms => rooms.GetRoomsByIds((session.GetHabbo().Messenger?.Friends.Values ?? [])
                .Where(friend => friend.Id != session.GetHabbo().Id).Select(friend => friend.CurrentRoom?.Id).OfType<uint>().Distinct().ToList()).Select(room => room.Data),
            NavigatorCategoryType.MyRights => Resolve(store.FindWithRights(session.GetHabbo().Id, limit)),
            NavigatorCategoryType.TopPromotions => rooms.GetOnGoingRoomPromotions(16, limit).Select(room => room.Data),
            NavigatorCategoryType.PromotionCategory => rooms.GetPromotedRooms(result.OrderId, limit).Select(room => room.Data),
            _ => []
        };
        var snapshots = selected.DistinctBy(room => room.Id).Select(room => RoomAppender.Capture(room, navigator)).ToImmutableArray();
        var action = NavigatorSearchAllowanceUtility.GetIntegerValue(result.SearchAllowance);

        return new(result.CategoryIdentifier, result.PublicName, action != 0 ? goBack : action,
            result.ViewMode == NavigatorViewMode.Thumbnail ? 1 : 0, snapshots);
    }

    private IEnumerable<RoomData> Query(string query)
    {
        if (query.StartsWith("owner:", StringComparison.OrdinalIgnoreCase)) {
            return Resolve(store.FindByOwnerName(query[6..]));
        }

        if (query.StartsWith("tag:", StringComparison.OrdinalIgnoreCase)) {
            return rooms.SearchTaggedRooms(query[4..]).Select(room => room.Data);
        }

        if (query.StartsWith("group:", StringComparison.OrdinalIgnoreCase)) {
            return rooms.SearchGroupRooms(query[6..]).Select(room => room.Data);
        }

        return query.Length == 0 ? [] : Resolve(store.FindByCaption(query).Where(room => room.Visible).Select(room => checked((uint)room.Id)));
    }

    private IEnumerable<RoomData> Resolve(IEnumerable<uint> ids)
    {
        foreach (var id in ids) {
            if (roomData.TryGetData(id, out var room)) {
                yield return room;
            }
        }
    }
}
