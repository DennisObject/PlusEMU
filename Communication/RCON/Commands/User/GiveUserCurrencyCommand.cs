using Plus.HabboHotel.Users;

namespace Plus.Communication.RCON.Commands.User;

internal class GiveUserCurrencyCommand : IRconCommand
{
    private readonly IUserMaintenanceService _maintenance;
    public string Description => "This command is used to give a user a specified amount of a specified currency.";

    public string Key => "give_user_currency";
    public string Parameters => "%userId% %currency% %amount%";

    public GiveUserCurrencyCommand(IUserMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (parameters.Length < 3 || !int.TryParse(parameters[0], out var userId) || !int.TryParse(parameters[2], out var amount))
        {
            return Task.FromResult(false);
        }

        return _maintenance.GiveCurrency(userId, parameters[1], amount);
    }
}
