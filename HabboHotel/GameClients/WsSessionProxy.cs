using NetCoreServer;

namespace Plus.HabboHotel.GameClients;

public class WsSessionProxy : WsSession
{
    private readonly GameClient _client;
    private readonly IGameClientManager _clientManager;
    public WsSessionProxy(WsServer server, GameClient client, IGameClientManager clientManager) : base(server)
    {
        _client = client;
        _clientManager = clientManager;
        _client.Id = Id;
        _clientManager.TrackClient(_client);
        _client.SendCallback = args =>
        {
            if (!Socket.Connected) return false;
            var buffer = args.MemoryBuffer.ToArray();
            return SendBinaryAsync(buffer, 0, buffer.Length);
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

    public override void OnWsReceived(byte[] buffer, long offset, long size) => _client.OnReceived(buffer, offset, size);
}
