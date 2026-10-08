using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands.User;

internal class TakeUserCurrencyCommand : IAcknowledgedRconCommand
{
    private readonly IUserMaintenanceService _maintenance;
    public string Description => "This command is used to take a specified amount of a specified currency from a user.";

    public string Key => "take_user_currency";
    public string Parameters => "%userId% %currency% %amount%";

    public TakeUserCurrencyCommand(IUserMaintenanceService maintenance)
    {
        _maintenance = maintenance;
    }

    public Task<GrantOutcome> Execute(string[] parameters)
    {
        if (parameters is not { Length: >= 3 } || !int.TryParse(parameters[0], out var userId) || !int.TryParse(parameters[2], out var amount)) {
            return Task.FromResult(GrantOutcome.Fail(GrantOutcome.InvalidPayload));
        }

        return _maintenance.TakeCurrency(userId, parameters[1], amount);
    }
}
