using Plus.HabboHotel.Permissions;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadRanksCommand : IRconCommand
{
    private readonly IAccessControl _permissionManager;
    public string Description => "This command is used to reload user permissions.";

    public string Key => "reload_ranks";
    public string Parameters => "";

    public ReloadRanksCommand(IAccessControl permissionManager)
    {
        _permissionManager = permissionManager;
    }
    public Task<bool> TryExecute(string[] parameters)
    {
        _permissionManager.Reload();

        return Task.FromResult(true);
    }
}
