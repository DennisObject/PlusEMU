using Plus.HabboHotel.Permissions;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;

namespace Plus.Communication.RCON.Commands.User;

internal class ReloadUserRankCommand : IRconCommand
{
    private readonly IAccessControl _accessControl;
    private readonly IGameClientManager _gameClientManager;
    private readonly IModerationManager _moderationManager;
    public string Description => "This command is used to reload a users rank and permissions.";

    public string Key => "reload_user_rank";
    public string Parameters => "%userId%";

    public ReloadUserRankCommand(IAccessControl accessControl, IGameClientManager gameClientManager, IModerationManager moderationManager)
    {
        _accessControl = accessControl;
        _gameClientManager = gameClientManager;
        _moderationManager = moderationManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (parameters.Length == 0 || !int.TryParse(parameters[0], out var userId))
            return Task.FromResult(false);
        var client = _gameClientManager.GetClientByUserId(userId);
        if (client == null || client.GetHabbo() == null)
            return Task.FromResult(false);
        _accessControl.Refresh(userId);
        if (client.GetHabbo().Access.Can(PermissionKeys.ModerationTickets))
        {
            client.Send(new ModeratorInitComposer(
                _moderationManager.UserMessagePresets,
                _moderationManager.RoomMessagePresets,
                _moderationManager.GetTickets));
        }
        return Task.FromResult(true);
    }
}