using Dapper;
using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using System.Data;
using Microsoft.Extensions.Logging;
using Plus.Database;

namespace Plus.HabboHotel.Games;

public class GameDataManager : IGameDataManager, IStartable
{
    private readonly IDatabase _database;
    private readonly ILogger<GameDataManager> _logger;

    private readonly Dictionary<int, GameData> _games;

    public GameDataManager(IDatabase database, ILogger<GameDataManager> logger)
    {
        _database = database;
        _logger = logger;
        _games = new();
    }

    public ICollection<GameData> GameData => _games.Values;

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var games = await connection.QueryAsync<GameRow>("SELECT id, name, colour_one AS ColourOne, colour_two AS ColourTwo, resource_path AS ResourcePath, string_three AS StringThree, game_swf AS Swf, game_assets AS Assets, game_server_host AS ServerHost, game_server_port AS ServerPort, game_enabled AS Enabled FROM games_config");
        _games.Clear();

        foreach (var game in games) {
            _games.Add(game.Id, new(game.Id, game.Name, game.ColourOne, game.ColourTwo, game.ResourcePath, game.StringThree, game.Swf, game.Assets, game.ServerHost, game.ServerPort, game.Enabled));
        }

        _logger.LogInformation("Game Data Manager -> LOADED");
    }

    private sealed class GameRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ColourOne { get; set; } = string.Empty;
        public string ColourTwo { get; set; } = string.Empty;
        public string ResourcePath { get; set; } = string.Empty;
        public string StringThree { get; set; } = string.Empty;
        public string Swf { get; set; } = string.Empty;
        public string Assets { get; set; } = string.Empty;
        public string ServerHost { get; set; } = string.Empty;
        public string ServerPort { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public bool TryGetGame(int gameId, [NotNullWhen(true)] out GameData? data) => _games.TryGetValue(gameId, out data);

    public int GetCount()
    {
        return _games.Values.Count(x => x.Enabled);
    }
}
