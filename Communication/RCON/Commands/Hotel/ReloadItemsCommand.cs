using Plus.HabboHotel.Items;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadItemsCommand : IRconCommand
{
    private readonly IItemDataManager _itemDataManager;
    public string Description => "This command is used to reload the game items.";

    public string Key => "reload_items";
    public string Parameters => "";

    public ReloadItemsCommand(IItemDataManager itemDataManager)
    {
        _itemDataManager = itemDataManager;
    }
    public Task<bool> TryExecute(string[] parameters)
    {
        _itemDataManager.Init();

        return Task.FromResult(true);
    }
}
