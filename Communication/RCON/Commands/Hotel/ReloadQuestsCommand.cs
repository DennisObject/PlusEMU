using Plus.HabboHotel.Quests;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadQuestsCommand : IRconCommand
{
    private readonly IQuestManager _questManager;
    public string Description => "This command is used to reload the quests manager.";

    public string Key => "reload_quests";
    public string Parameters => "";

    public ReloadQuestsCommand(IQuestManager questManager)
    {
        _questManager = questManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _questManager.Init();

        return Task.FromResult(true);
    }
}
