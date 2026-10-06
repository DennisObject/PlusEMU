using Plus.Core;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.AI.Responses;
using Plus.Database;

namespace Plus.HabboHotel.Bots;

public class BotManager : IBotManager, IStartable
{
    private readonly IDatabase _database;
    private readonly ILogger<BotManager> _logger;

    private List<BotResponse> _responses;

    public BotManager(IDatabase database, ILogger<BotManager> logger)
    {
        _database = database;
        _logger = logger;
        _responses = new();
    }

    public int StartOrder => 20;
    public Task Start() => Init();

    public async Task Init()
    {
        using var connection = _database.Connection();
        var data = await connection.QueryAsync<(string BotAi, string Keywords, string ResponseText, string ResponseMode, string ResponseBeverage)>(
            "SELECT `bot_ai`, `chat_keywords` as keywords, `response_text`,`response_mode`,`response_beverage` FROM `bots_responses`");
        _responses = data.Select(row => new BotResponse(row.BotAi, row.Keywords, row.ResponseText, row.ResponseMode, row.ResponseBeverage)).ToList();
    }

    public BotResponse? GetResponse(BotAiType type, string message) => _responses.Where(x => x.AiType == type).FirstOrDefault(response => response.KeywordMatched(message));
}
