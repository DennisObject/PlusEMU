using Plus.Core;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.Utilities;
using Dapper;

namespace Plus.HabboHotel.Items.Televisions;

public class TelevisionManager : ITelevisionManager, IStartable
{
    private readonly ILogger<TelevisionManager> _logger;
    private readonly IDatabase _database;

    public TelevisionManager(ILogger<TelevisionManager> logger, IDatabase database)
    {
        _logger = logger;
        _database = database;
    }

    public Dictionary<int, TelevisionItem> Televisions { get; } = new();


    public ICollection<TelevisionItem> TelevisionList => Televisions.Values;

    public int StartOrder => 20;
    public Task Start() => LoadAsync();
    public void Init() => LoadAsync().GetAwaiter().GetResult();

    private async Task LoadAsync()
    {
        if (Televisions.Count > 0)
        {
            Televisions.Clear();
        }

        using (var connection = _database.Connection())
        {
            var rows = await connection.QueryAsync<TelevisionRow>("SELECT `id`, `youtube_id` AS YoutubeId, `title`, `description`, `enabled` FROM `items_youtube` ORDER BY `id` DESC");

            foreach (var row in rows)
            {
                Televisions.Add(row.Id, new(row.Id, row.YoutubeId, row.Title, row.Description, row.Enabled));
            }
        }

        _logger.LogInformation("Television Items -> LOADED");
    }

    private sealed record TelevisionRow(int Id, string YoutubeId, string Title, string Description, bool Enabled);

    public bool TryGet(int itemId, [NotNullWhen(true)] out TelevisionItem? televisionItem)
    {
        if (Televisions.TryGetValue(itemId, out televisionItem))
        {
            return true;
        }

        return false;
    }
}
