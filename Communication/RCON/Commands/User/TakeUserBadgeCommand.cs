using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class TakeUserBadgeCommand : IRconCommand
{
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to take a badge from a user.";

    public string Key => "take_user_badge";
    public string Parameters => "%userId% %badgeId%";

    public TakeUserBadgeCommand(IGameClientManager gameClientManager)
    {
        _gameClientManager = gameClientManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId))
        {
            return Task.FromResult(false);
        }

        var client = _gameClientManager.GetClientByUserId(userId);

        if (client?.GetHabbo() == null)
        {
            return Task.FromResult(false);
        }

        // Validate the badge
        if (string.IsNullOrEmpty(Convert.ToString(parameters[1])))
        {
            return Task.FromResult(false);
        }

        var badge = Convert.ToString(parameters[1]);

        if (client.GetHabbo().Inventory.Badges.HasBadge(badge))
        {
            client.GetHabbo().Inventory.Badges.RemoveBadge(badge);
        }

        return Task.FromResult(true);
    }
}
