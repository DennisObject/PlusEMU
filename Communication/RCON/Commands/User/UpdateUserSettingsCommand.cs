using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands.User;

internal class UpdateUserSettingsCommand : IAcknowledgedRconCommand
{
    private readonly IUserGrantService _grants;
    public string Description => "This command is used to set the home room and friend bar state a logout would otherwise overwrite.";

    public string Key => "update_user_settings";
    public string Parameters => "%userId% %base64Json%";

    public UpdateUserSettingsCommand(IUserGrantService grants)
    {
        _grants = grants;
    }

    public Task<GrantOutcome> Execute(string[] parameters)
    {
        if (parameters is not { Length: 2 } || !int.TryParse(parameters[0], out var userId)) {
            return Task.FromResult(GrantOutcome.Fail(GrantOutcome.InvalidPayload));
        }

        return _grants.UpdateSettings(userId, parameters[1]);
    }
}
