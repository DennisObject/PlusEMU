using Plus.HabboHotel.Users.Grants;

namespace Plus.Communication.RCON.Commands.User;

internal class GiveUserBadgeCommand : IAcknowledgedRconCommand
{
    private readonly IUserGrantService _grants;
    public string Description => "This command is used to give a user a badge.";

    public string Key => "give_user_badge";
    public string Parameters => "%userId% %badgeId%";

    public GiveUserBadgeCommand(IUserGrantService grants)
    {
        _grants = grants;
    }

    public Task<GrantOutcome> Execute(string[] parameters)
    {
        if (parameters is not { Length: >= 2 } || !int.TryParse(parameters[0], out var userId)) {
            return Task.FromResult(GrantOutcome.Fail(GrantOutcome.InvalidPayload));
        }

        return _grants.GiveBadge(userId, parameters[1]);
    }
}
