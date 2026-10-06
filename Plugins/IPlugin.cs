using Plus.Utilities.DependencyInjection;

namespace Plus.Plugins;

[Singleton]
public interface IPlugin
{
    void Start();
}
