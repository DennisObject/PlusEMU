using Plus.Utilities.DependencyInjection;

namespace Plus.Core;

[Singleton]
public interface IStartable
{
    int StartOrder => 0;
    Task Start();
}
