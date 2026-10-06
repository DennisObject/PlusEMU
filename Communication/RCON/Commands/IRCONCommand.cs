using Plus.Utilities.DependencyInjection;

namespace Plus.Communication.RCON.Commands;

[Singleton]
public interface IRconCommand
{
    string Key
    {
        get;
    }
    string Parameters
    {
        get;
    }
    string Description
    {
        get;
    }
    Task<bool> TryExecute(string[] parameters);
}
