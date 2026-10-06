using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class AlertUserCommand : IRconCommand
{
    private readonly IGameClientManager _gameClientManager;
    public string Key => "alert_user";
    public string Description => "This command is used to alert a user.";

    public string Parameters => "%userId% %message%";

    public AlertUserCommand(IGameClientManager gameClientManager)
    {
        _gameClientManager = gameClientManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        if (!int.TryParse(parameters[0], out var userId)) {
            return Task.FromResult(false);
        }

        var client = _gameClientManager.GetClientByUserId(userId);

        if (client == null || client.GetHabbo() == null) {
            return Task.FromResult(false);
        }

        // Validate the message
        if (string.IsNullOrEmpty(Convert.ToString(parameters[1]))) {
            return Task.FromResult(false);
        }

        var message = Convert.ToString(parameters[1]);
        client.Send(new BroadcastMessageAlertComposer(message));

        return Task.FromResult(true);
    }
}
