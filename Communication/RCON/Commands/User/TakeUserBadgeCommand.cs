using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands.User;

internal class TakeUserBadgeCommand : IAcknowledgedRconCommand
{
    private readonly IUserGrantService _grants;
    public string Description => "This command is used to take a badge from an offline user.";

    public string Key => "take_user_badge";
    public string Parameters => "%userId% %badgeId%";

    public TakeUserBadgeCommand(IUserGrantService grants)
    {
        _grants = grants;
    }

    public Task<GrantOutcome> Execute(string[] parameters)
    {
        if (parameters is not { Length: >= 2 } || !int.TryParse(parameters[0], out var userId)) {
            return Task.FromResult(GrantOutcome.Fail(GrantOutcome.InvalidPayload));
        }

        return _grants.TakeBadge(userId, parameters[1]);
    }
}
