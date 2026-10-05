using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Communication.Packets.Outgoing.Navigator.New;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Navigator;

public interface INavigatorPresentationService
{
    void InitializeNewNavigator(GameClient session);
    void ShowUserFlatCategories(GameClient session);
    void ShowEventCategories(GameClient session);
}

public sealed class NavigatorPresentationService(INavigatorManager navigator) : INavigatorPresentationService
{
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
        var access = session.GetHabbo().Access;
        var rows = navigator.FlatCategories.Select(category => NavigatorCategoryRow.CaptureForUser(category, access)).ToImmutableArray();
        session.Send(new UserFlatCatsComposer(rows));
    }

    public void ShowEventCategories(GameClient session)
    {
        var rows = navigator.EventCategories.Select(NavigatorCategoryRow.CaptureEvent).ToImmutableArray();
        session.Send(new NavigatorFlatCatsComposer(rows));
    }
}
