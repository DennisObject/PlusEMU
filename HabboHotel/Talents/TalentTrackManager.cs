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

    private readonly Dictionary<(string Type, int Level), TalentTrackLevel> _levels;

    public TalentTrackManager(ILogger<TalentTrackManager> logger, IDatabase database)
    {
        _logger = logger;
        _database = database;
        _levels = new();
    }

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var levels = await connection.QueryAsync<LevelRow>("SELECT type, level, data_actions AS Actions, data_gifts AS Gifts FROM talents ORDER BY type, level");
        var subLevels = await connection.QueryAsync<TaskRow>("SELECT talent_type AS Type, talent_level AS TalentLevel, sub_level AS Level, badge_code AS Badge, required_progress AS Progress FROM talents_sub_levels ORDER BY talent_type, talent_level, sub_level");
        var byLevel = subLevels.ToLookup(row => (row.Type, row.TalentLevel));
        _levels.Clear();

        foreach (var level in levels) {
            _levels.Add((level.Type, level.Level), new(level.Type, level.Level, level.Actions, level.Gifts,
                byLevel[(level.Type, level.Level)].Select(row => new TalentTrackSubLevel(row.Level, row.Badge, row.Progress))));
        }

        _logger.LogInformation("Loaded {Count} talent track levels", _levels.Count);
    }

    public ICollection<TalentTrackLevel> GetLevels() => _levels.Values;

    private sealed class LevelRow
    {
        public string Type { get; set; } = "";
        public int Level { get; set; }
        public string Actions { get; set; } = "";
        public string Gifts { get; set; } = "";
    }

    private sealed class TaskRow
    {
        public string Type { get; set; } = "";
        public int TalentLevel { get; set; }
        public int Level { get; set; }
        public string Badge { get; set; } = "";
        public int Progress { get; set; }
    }
}
