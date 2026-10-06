using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.Communication.RCON.Commands.Hotel;

internal class ReloadCatalogCommand : IRconCommand
{
    private readonly ICatalogManager _catalogManager;
    private readonly IGameClientManager _gameClientManager;
    public string Description => "This command is used to reload the catalog.";

    public string Key => "reload_catalog";
    public string Parameters => "";

    public ReloadCatalogCommand(ICatalogManager catalogManager, IGameClientManager gameClientManager)
    {
        _catalogManager = catalogManager;
        _gameClientManager = gameClientManager;
    }

    public Task<bool> TryExecute(string[] parameters)
    {
        _catalogManager.Init();
        _gameClientManager.SendPacket(new CatalogUpdatedComposer());

        return Task.FromResult(true);
    }
}
