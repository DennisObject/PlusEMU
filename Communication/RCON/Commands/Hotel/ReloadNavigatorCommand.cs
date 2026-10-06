using Plus.HabboHotel.Navigator;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadNavigatorCommand : IRconCommand
{
    private readonly INavigatorManager _navigatorManager;
    public string Description => "This command is used to reload the navigator.";

    public string Key => "reload_navigator";
    public string Parameters => "";

    public ReloadNavigatorCommand(INavigatorManager navigatorManager)
    {
        _navigatorManager = navigatorManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _navigatorManager.Init();

        return Task.FromResult(true);
    }
}
