using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class ReloadUserCurrencyCommand : IRconCommand
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to update the users currency from the database.";

    public string Key => "reload_user_currency";
    public string Parameters => "%userId% %currency%";

    public ReloadUserCurrencyCommand(IDatabase database, IGameClientManager gameClientManager)
    {
        _database = database;
        _gameClientManager = gameClientManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        var client = _gameClientManager.GetClientByUserId(userId);
        if (client == null || client.GetHabbo() == null)
            return Task.FromResult(false);

        // Validate the currency type
        if (string.IsNullOrEmpty(Convert.ToString(parameters[1])))
            return Task.FromResult(false);
        var currency = Convert.ToString(parameters[1]);
        lock (client.GetHabbo().WalletSync)
        {
            if (client.GetHabbo().WalletClosed) return Task.FromResult(false);
            switch (currency)
            {
                default:
                    return Task.FromResult(false);
                case "coins":
                case "credits":
                {
                    using var connection = _database.Connection();
                    var credits = connection.QuerySingleOrDefault<int?>("SELECT `credits` FROM `users` WHERE `id` = @id", new { id = userId });
                    if (!credits.HasValue) return Task.FromResult(false);
                    client.GetHabbo().Credits = credits.Value;
                    client.Send(new CreditBalanceComposer(client.GetHabbo().Credits));
                    break;
                }
                case "pixels":
                case "duckets":
                {
                    using var connection = _database.Connection();
                    var duckets = connection.QuerySingleOrDefault<int?>("SELECT `activity_points` FROM `users` WHERE `id` = @id", new { id = userId });
                    if (!duckets.HasValue) return Task.FromResult(false);
                    client.GetHabbo().Duckets = duckets.Value;
                    client.Send(new HabboActivityPointNotificationComposer(client.GetHabbo().Duckets, duckets.Value));
                    break;
                }
                case "diamonds":
                {
                    using var connection = _database.Connection();
                    var diamonds = connection.QuerySingleOrDefault<int?>("SELECT `vip_points` FROM `users` WHERE `id` = @id", new { id = userId });
                    if (!diamonds.HasValue) return Task.FromResult(false);
                    client.GetHabbo().Diamonds = diamonds.Value;
                    client.Send(new HabboActivityPointNotificationComposer(diamonds.Value, 0, 5));
                    break;
                }
                case "gotw":
                {
                    using var connection = _database.Connection();
                    var gotw = connection.QuerySingleOrDefault<int?>("SELECT `gotw_points` FROM `users` WHERE `id` = @id", new { id = userId });
                    if (!gotw.HasValue) return Task.FromResult(false);
                    client.GetHabbo().GotwPoints = gotw.Value;
                    client.Send(new HabboActivityPointNotificationComposer(gotw.Value, 0, 103));
                    break;
                }
            }
        }
        return Task.FromResult(true);
    }
}
