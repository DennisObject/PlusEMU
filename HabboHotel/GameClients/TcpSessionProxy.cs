using NetCoreServer;

namespace Plus.HabboHotel.GameClients;

public class TcpSessionProxy : TcpSession
{
    private readonly GameClient _client;
    private readonly IGameClientManager _clientManager;
    public TcpSessionProxy(TcpServer server, GameClient client, IGameClientManager clientManager) : base(server)
    {
        _client = client;
        _clientManager = clientManager;
        _client.Id = Id;
        _clientManager.TrackClient(_client);
        _client.SendCallback = args =>
        {
            if (!Socket.Connected) return false;
            try
            {
                return Socket.SendAsync(args);
            }
            catch (Exception e) // TODO 80O: Maybe handle some potential errors.
            {
            }
            return false;
        };
        _client.DisconnectRequested = () => Disconnect();
    }

    protected override void OnConnected()
    {
        base.OnConnected();
    }

    protected override void OnDisconnected()
    {
        _clientManager.ReleaseClient(Id);
        _client.OnDisconnected();
    }

    protected override void OnReceived(byte[] buffer, long offset, long size) => _client.OnReceived(buffer, offset, size);
}
