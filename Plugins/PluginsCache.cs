using Plus.Core;

namespace Plus.Plugins;

public class PluginsCache : IPluginsCache, IStartable
{
    public IReadOnlyDictionary<IPluginDefinition, IPlugin> Plugins => _plugins;
    private readonly Dictionary<IPluginDefinition, IPlugin> _plugins;
    public PluginsCache(IEnumerable<IPluginDefinition> pluginDefinitions, IEnumerable<IPlugin> plugins)
    {
        _plugins = pluginDefinitions.ToDictionary(kvp => kvp, kvp => plugins.First(p => p.GetType() == kvp.PluginClass));
    }

    public int StartOrder => 100;

    public Task Start() => Task.WhenAll(_plugins.Values.Select(plugin => Task.Run(plugin.Start)));
}