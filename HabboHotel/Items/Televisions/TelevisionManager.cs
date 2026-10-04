using Plus.Core;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.Utilities;

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
    public Task Start()
    {
        Init();
        return Task.CompletedTask;
    }

    public void Init()
    {
        if (Televisions.Count > 0)
            Televisions.Clear();
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery("SELECT * FROM `items_youtube` ORDER BY `id` DESC");
            var getData = dbClient.GetTable();
            if (getData != null)
            {
                foreach (DataRow row in getData.Rows)
                {
                    Televisions.Add(Convert.ToInt32(row["id"]),
                        new(Convert.ToInt32(row["id"]), row["youtube_id"].ToString(), row["title"].ToString(), row["description"].ToString(),
                            ConvertExtensions.EnumToBool(row["enabled"].ToString())));
                }
            }
        }
        _logger.LogInformation("Television Items -> LOADED");
    }

    public bool TryGet(int itemId, [NotNullWhen(true)] out TelevisionItem? televisionItem)
    {
        if (Televisions.TryGetValue(itemId, out televisionItem))
            return true;
        return false;
    }
}