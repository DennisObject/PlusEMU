using Plus.HabboHotel.Rooms.Chat;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadFilterCommand : IRconCommand
{
    private readonly IChatManager _chatManager;
    public string Description => "This command is used to reload the chatting filter manager.";

    public string Key => "reload_filter";
    public string Parameters => "";

    public ReloadFilterCommand(IChatManager chatManager)
    {
        _chatManager = chatManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _chatManager.GetFilter().Init();

        return Task.FromResult(true);
    }
}
