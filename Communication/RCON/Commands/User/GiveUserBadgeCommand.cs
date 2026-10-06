using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class GiveUserBadgeCommand : IRconCommand
{
    private readonly IBadgeManager _badgeManager;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to give a user a badge.";

    public string Key => "give_user_badge";
    public string Parameters => "%userId% %badgeId%";

    public GiveUserBadgeCommand(IBadgeManager badgeManager, IGameClientManager gameClientManager)
    {
        _badgeManager = badgeManager;
        _gameClientManager = gameClientManager;
    }

    public async Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId))
        {
            return false;
        }

        var client = _gameClientManager.GetClientByUserId(userId);

        if (client == null || client.GetHabbo() == null)
        {
            return false;
        }

        // Validate the badge
        if (string.IsNullOrEmpty(Convert.ToString(parameters[1])))
        {
            return false;
        }

        var badge = Convert.ToString(parameters[1]);

        if (!client.GetHabbo().Inventory.Badges.HasBadge(badge))
        {
            await _badgeManager.GiveBadge(client.GetHabbo(), badge);
            client.Send(new BroadcastMessageAlertComposer("You have been given a new badge!"));
        }

        return true;
    }
}
