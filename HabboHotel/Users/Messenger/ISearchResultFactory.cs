namespace Plus.HabboHotel.Users.Messenger;

public interface ISearchResultFactory
{
    List<SearchResult> GetSearchResult(string query);
}
