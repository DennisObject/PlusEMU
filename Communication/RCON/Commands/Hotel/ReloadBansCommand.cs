using Plus.HabboHotel.Moderation;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadBansCommand : IRconCommand
{
    private readonly IModerationManager _moderationManager;
    public string Description => "This command is used to re-cache the bans.";

    public string Key => "reload_bans";
    public string Parameters => "";

    public ReloadBansCommand(IModerationManager moderationManager)
    {
        _moderationManager = moderationManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _moderationManager.ReCacheBans();

        return Task.FromResult(true);
    }
}
