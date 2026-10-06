using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Rooms.Session;
using Plus.HabboHotel.Rooms;
using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorPresentationService
{
    void InitializeNewNavigator(GameClient session);
    void ShowUserFlatCategories(GameClient session);
    void ShowEventCategories(GameClient session);
    void FindFriends(GameClient session);
}

public sealed class NavigatorPresentationService(INavigatorManager navigator, IRoomManager rooms) : INavigatorPresentationService
{
    public void FindFriends(GameClient session)
    {
        var roomId = rooms.TryGetRandomLoadedRoom()?.Id;
        session.Send(new FindFriendsProcessResultComposer(roomId.HasValue));

        if (roomId is { } id)
        {
            session.Send(new RoomForwardComposer(id));
        }
    }

    public void InitializeNewNavigator(GameClient session)
    {
        var searchCodes = navigator.TopLevelItems.Select(item => item.SearchCode).ToImmutableArray();
        session.Send(new NavigatorMetaDataParserComposer(searchCodes));
        session.Send(new NavigatorLiftedRoomsComposer());
        session.Send(new NavigatorCollapsedCategoriesComposer());
        session.Send(new NavigatorPreferencesComposer());
    }

    public void ShowUserFlatCategories(GameClient session)
    {
        var permissions = session.GetHabbo().Access.Keys;
        var rows = navigator.FlatCategories.Select(category => NavigatorCategoryRow.CaptureForUser(category, permissions)).ToImmutableArray();
        session.Send(new UserFlatCatsComposer(rows));
    }

    public void ShowEventCategories(GameClient session)
    {
        var rows = navigator.EventCategories.Select(NavigatorCategoryRow.CaptureEvent).ToImmutableArray();
        session.Send(new NavigatorFlatCatsComposer(rows));
    }
}
