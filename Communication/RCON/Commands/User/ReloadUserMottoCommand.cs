using Plus.HabboHotel.Users;

namespace Plus.Communication.RCON.Commands.User;

internal class ReloadUserMottoCommand : IRconCommand
{
    private readonly IUserMaintenanceService _maintenance;
    public string Description => "This command is used to reload the users motto from the database.";

    public string Key => "reload_user_motto";
    public string Parameters => "%userId%";

    public ReloadUserMottoCommand(IUserMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (parameters.Length < 1 || !int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        return _maintenance.ReloadMotto(userId);
    }
}