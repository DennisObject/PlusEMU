using Plus.Communication.RCON.Commands;

namespace Plus.Communication.RCON;

public interface IRconSocket
{
    void Init(string host, int port, IEnumerable<string> allowedConnections);
    ICommandManager GetCommands();
}
