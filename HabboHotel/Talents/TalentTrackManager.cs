using Dapper;
using Plus.Core;
using System.Data;
using Microsoft.Extensions.Logging;
using Plus.Database;

namespace Plus.HabboHotel.Talents;

public class TalentTrackManager : ITalentTrackManager, IStartable
{
    private readonly ILogger<TalentTrackManager> _logger;
    private readonly IDatabase _database;

    private readonly Dictionary<int, TalentTrackLevel> _citizenshipLevels;

    public TalentTrackManager(ILogger<TalentTrackManager> logger, IDatabase database)
    {
        _logger = logger;
        _database = database;
        _citizenshipLevels = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var levels = await connection.QueryAsync<(string Type, int Level, string Actions, string Gifts)>("SELECT type, level, data_actions, data_gifts FROM talents");
        var subLevels = await connection.QueryAsync<(int TalentLevel, int Level, string Badge, int Progress)>("SELECT talent_level, sub_level, badge_code, required_progress FROM talents_sub_levels");
        var byLevel = subLevels.ToLookup(row => row.TalentLevel);
        _citizenshipLevels.Clear();

        foreach (var level in levels)
        {
            _citizenshipLevels.Add(level.Level, new(level.Type, level.Level, level.Actions, level.Gifts,
                byLevel[level.Level].Select(row => new TalentTrackSubLevel(row.Level, row.Badge, row.Progress))));
        }

        _logger.LogInformation("Loaded {Count} talent track levels", _citizenshipLevels.Count);
    }

    public ICollection<TalentTrackLevel> GetLevels() => _citizenshipLevels.Values;
}
