using Plus.HabboHotel.GameClients;

namespace Plus.Communication.RCON.Commands.User;

internal class DisconnectUserCommand : IRconCommand
{
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to Disconnect a user.";

    public string Key => "disconnect_user";
    public string Parameters => "%userId%";

    public DisconnectUserCommand(IGameClientManager gameClientManager)
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

        client.Disconnect();

        return Task.FromResult(true);
    }
}
