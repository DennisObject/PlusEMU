using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Database;
using Plus.HabboHotel.LandingView.Promotions;

namespace Plus.HabboHotel.LandingView;

public class LandingViewManager : ILandingViewManager, IStartable
{
    private readonly IDatabase _database;
    private readonly ILogger<LandingViewManager> _logger;

    private Dictionary<int, Promotion> _promotionItems = new();

    public LandingViewManager(IDatabase database, ILogger<LandingViewManager> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task Reload()
    {
        using var connection = _database.Connection();
        _promotionItems = (await connection.QueryAsync<Promotion>("SELECT * FROM `server_landing` ORDER BY `id` DESC")).ToDictionary(promotion => promotion.Id);
        _logger.LogInformation("Landing View Manager -> LOADED");
    }

    public ICollection<Promotion> GetPromotionItems() => _promotionItems.Values;
    public Task Start() => Reload();
}
