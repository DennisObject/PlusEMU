using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands.User;

internal class GrantUserBundleCommand : IAcknowledgedRconCommand
{
    private readonly IUserGrantService _grants;
    public string Description => "This command is used to grant an offline user credits, currencies, furniture, badges and a rank at once, exactly once per key.";

    public string Key => "grant_user_bundle";
    public string Parameters => "%userId% %idempotencyKey% %base64Json%";

    public GrantUserBundleCommand(IUserGrantService grants)
    {
        _grants = grants;
    }

    public Task<GrantOutcome> Execute(string[] parameters)
    {
        if (parameters is not { Length: 3 } || !int.TryParse(parameters[0], out var userId)) {
            return Task.FromResult(GrantOutcome.Fail(GrantOutcome.InvalidPayload));
        }

        return _grants.GrantBundle(userId, parameters[1], parameters[2]);
    }
}
