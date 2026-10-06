namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadServerSettingsCommand : IRconCommand
{
    public string Description => "This command is used to reload the server settings.";

    public string Key => "reload_server_settings";
    public string Parameters => "";

    public Task<bool> TryExecute(string[] parameters)
    {
        PlusEnvironment.SettingsManager.Reload();

        return Task.FromResult(true);
    }
}
