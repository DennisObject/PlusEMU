using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Database;
using Dapper;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class TakeUserCurrencyCommand : IRconCommand
{
    private readonly IDatabase _database;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to take a specified amount of a specified currency from a user.";

    public string Key => "take_user_currency";
    public string Parameters => "%userId% %currency% %amount%";

    public TakeUserCurrencyCommand(IDatabase database, IGameClientManager gameClientManager)
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
        if (!int.TryParse(parameters[2], out var amount))
            return Task.FromResult(false);
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
                    client.GetHabbo().Credits -= amount;
                    using (var connection = _database.Connection())
                        connection.Execute("UPDATE `users` SET `credits` = @credits WHERE `id` = @id", new { credits = client.GetHabbo().Credits, id = userId });
                    client.Send(new CreditBalanceComposer(client.GetHabbo().Credits));
                    break;
                }
                case "pixels":
                case "duckets":
                {
                    client.GetHabbo().Duckets -= amount;
                    using (var connection = _database.Connection())
                        connection.Execute("UPDATE `users` SET `activity_points` = @duckets WHERE `id` = @id", new { duckets = client.GetHabbo().Duckets, id = userId });
                    client.Send(new HabboActivityPointNotificationComposer(client.GetHabbo().Duckets, amount));
                    break;
                }
                case "diamonds":
                {
                    client.GetHabbo().Diamonds -= amount;
                    using (var connection = _database.Connection())
                        connection.Execute("UPDATE `users` SET `vip_points` = @diamonds WHERE `id` = @id", new { diamonds = client.GetHabbo().Diamonds, id = userId });
                    client.Send(new HabboActivityPointNotificationComposer(client.GetHabbo().Diamonds, 0, 5));
                    break;
                }
                case "gotw":
                {
                    client.GetHabbo().GotwPoints -= amount;
                    using (var connection = _database.Connection())
                        connection.Execute("UPDATE `users` SET `gotw_points` = @gotw WHERE `id` = @id", new { gotw = client.GetHabbo().GotwPoints, id = userId });
                    client.Send(new HabboActivityPointNotificationComposer(client.GetHabbo().GotwPoints, 0, 103));
                    break;
                }
            }
        }
        return Task.FromResult(true);
    }
}
