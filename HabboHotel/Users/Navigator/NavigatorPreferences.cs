using Plus.HabboHotel.Users.Navigator.SavedSearches;
using System.Collections.Concurrent;

namespace Plus.HabboHotel.Users.Navigator;

public class NavigatorPreferences
{

    private readonly ConcurrentDictionary<int, SavedSearch> _savedSearches;

    public NavigatorPreferences(ConcurrentDictionary<int, SavedSearch> savedSearches)
    {
        _savedSearches = savedSearches;
    }
}
