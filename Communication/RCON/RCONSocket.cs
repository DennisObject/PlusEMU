using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Plus.Communication.RCON.Commands;

namespace Plus.Communication.RCON;

public class RconSocket : IRconSocket
{
    private IReadOnlyList<string> _allowedConnections = [];
    private readonly ICommandManager _commands;
    private readonly ILogger<RconConnection> _connectionLogger;

    public RconSocket(ICommandManager commandManager, ILogger<RconConnection> connectionLogger)
    {
        _commands = commandManager;
        _connectionLogger = connectionLogger;
    }

    public void Init(string host, int port, IEnumerable<string> allowedConnections)
    {
        // Swap in a complete list so the accept callback never sees a partly filled one.
        _allowedConnections = allowedConnections.ToList();

        try {
            var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Parse(host), port));
            listener.Listen(0);
            listener.BeginAccept(OnCallBack, listener);
        }
        catch (Exception e) {
            throw new ArgumentException($"Could not set up Rcon socket:\n{e}");
        }
    }

    private void OnCallBack(IAsyncResult iAr)
    {
        if (iAr.AsyncState is not Socket listener) {
            return;
        }

        try {
            var socket = listener.EndAccept(iAr);
            var ip = (socket.RemoteEndPoint as IPEndPoint)?.Address.ToString();

            if (ip != null && _allowedConnections.Contains(ip)) {
                new RconConnection(socket, _connectionLogger);
            }
            else {
                socket.Close();
            }
        }
        catch (Exception) {
            // ignored
        }

        listener.BeginAccept(OnCallBack, listener);
    }

    public ICommandManager GetCommands() => _commands;
}
