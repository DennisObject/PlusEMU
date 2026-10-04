using Plus.Database;
using Dapper;

namespace Plus.HabboHotel.Users.Messenger;

public class SearchResultFactory : ISearchResultFactory
{
    private readonly IDatabase _database;

    public SearchResultFactory(IDatabase database)
    {
        _database = database;
    }

    public List<SearchResult> GetSearchResult(string query)
    {
        using var connection = _database.Connection();
        return connection.Query<SearchResultRow>(
                "SELECT `id`, `username`, `motto`, `look`, `last_online` AS LastOnline FROM `users` WHERE `username` LIKE @query LIMIT 50",
                new { query = $"{query}%" })
            .Select(row => new SearchResult(row.Id, row.Username, row.Motto, row.Look, row.LastOnline))
            .ToList();
    }

    private sealed record SearchResultRow(int Id, string Username, string Motto, string Look, DateTimeOffset? LastOnline);
}
