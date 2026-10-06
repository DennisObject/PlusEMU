using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class SyncUserCurrencyCommand : IRconCommand
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to sync a users specified currency to the database.";

    public string Key => "sync_user_currency";
    public string Parameters => "%userId% %currency%";

    public SyncUserCurrencyCommand(IDatabase database, IGameClientManager gameClientManager)
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
                    connection.Execute("UPDATE `users` SET `credits` = @credits WHERE `id` = @id", new { credits = client.GetHabbo().Credits, id = userId });
                    break;
                }
                case "pixels":
                case "duckets":
                {
                    using var connection = _database.Connection();
                    connection.Execute("UPDATE `users` SET `activity_points` = @duckets WHERE `id` = @id", new { duckets = client.GetHabbo().Duckets, id = userId });
                    break;
                }
                case "diamonds":
                {
                    using var connection = _database.Connection();
                    connection.Execute("UPDATE `users` SET `vip_points` = @diamonds WHERE `id` = @id", new { diamonds = client.GetHabbo().Diamonds, id = userId });
                    break;
                }
                case "gotw":
                {
                    using var connection = _database.Connection();
                    connection.Execute("UPDATE `users` SET `gotw_points` = @gotw WHERE `id` = @id", new { gotw = client.GetHabbo().GotwPoints, id = userId });
                    break;
                }
            }
        }
        return Task.FromResult(true);
    }
}
