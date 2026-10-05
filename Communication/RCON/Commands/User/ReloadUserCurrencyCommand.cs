using Plus.HabboHotel.Users;

namespace Plus.Communication.RCON.Commands.User;

internal class ReloadUserCurrencyCommand : IRconCommand
{
    private readonly IUserMaintenanceService _maintenance;
    public string Description => "This command is used to update the users currency from the database.";

    public string Key => "reload_user_currency";
    public string Parameters => "%userId% %currency%";

    public ReloadUserCurrencyCommand(IUserMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (parameters.Length < 2 || !int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        return _maintenance.ReloadCurrency(userId, parameters[1]);
    }
}