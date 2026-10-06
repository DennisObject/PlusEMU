using Plus.HabboHotel.Users;

namespace Plus.Communication.RCON.Commands.User;

internal class SyncUserCurrencyCommand : IRconCommand
{
    private readonly IUserMaintenanceService _maintenance;
    public string Description => "This command is used to sync a users specified currency to the database.";

    public string Key => "sync_user_currency";
    public string Parameters => "%userId% %currency%";

    public SyncUserCurrencyCommand(IUserMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (parameters.Length < 2 || !int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        return _maintenance.SyncCurrency(userId, parameters[1]);
    }
}
