using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Catalog;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Catalog.Admin;

public interface ICatalogCacheRefresher
{
    // Reloads the catalog (and the furniture definitions when asked) like :update catalog, then tells every
    // client the catalog changed. Runs after the editor has answered: a full reload can outlast the packet deadline.
    void Schedule(bool reloadItems = false);
}

public sealed class CatalogCacheRefresher : ICatalogCacheRefresher
{
    private readonly ICatalogManager _catalogManager;
    private readonly IItemDataManager _itemDataManager;
    private readonly IGameClientManager _gameClientManager;
    private readonly ILogger<CatalogCacheRefresher> _logger;
    private readonly object _sync = new();
    private bool _running;
    private bool _pending;
    private bool _pendingItems;

    public CatalogCacheRefresher(ICatalogManager catalogManager, IItemDataManager itemDataManager, IGameClientManager gameClientManager, ILogger<CatalogCacheRefresher> logger)
    {
        _catalogManager = catalogManager;
        _itemDataManager = itemDataManager;
        _gameClientManager = gameClientManager;
        _logger = logger;
    }

    public void Schedule(bool reloadItems = false)
    {
        lock (_sync)
        {
            _pendingItems |= reloadItems;
            _pending = true;

            if (_running)
            {
                return;
            }

            _running = true;
        }

        _ = Task.Run(RunAsync);
    }

    // Saves made while a reload runs are picked up by one more reload, not one per save.
    private async Task RunAsync()
    {
        while (true)
        {
            bool reloadItems;

            lock (_sync)
            {
                if (!_pending)
                {
                    _running = false;

                    return;
                }

                reloadItems = _pendingItems;
                _pending = _pendingItems = false;
            }

            try
            {
                if (reloadItems)
                {
                    _itemDataManager.Init();
                }

                await _catalogManager.Init();
                _gameClientManager.SendPacket(new CatalogUpdatedComposer());
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Catalog editor cache refresh failed");
            }
        }
    }
}
