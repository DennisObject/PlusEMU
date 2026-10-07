using NetCoreServer;

namespace Plus.HabboHotel.GameClients;

public class WsSessionProxy : WsSession
{
    private readonly GameClient _client;
    public WsSessionProxy(WsServer server, GameClient client) : base(server)
    {
        _client = client;
        _client.Id = Id;
        _client.SendCallback = args =>
        {
            if (!Socket.Connected) {
                return false;
            }

            var buffer = args.MemoryBuffer.ToArray();
            SendBinaryAsync(buffer, 0, buffer.Length);

            // The WebSocket queue owns a copy; no SocketAsyncEventArgs operation is pending.
            return false;
        };
        _client.DisconnectRequested = () => Disconnect();
    }

    protected override void OnConnected()
    {
        base.OnConnected();
    }

    protected override void OnDisconnected() => _client.OnTransportDisconnected();

    public override void OnWsReceived(byte[] buffer, long offset, long size) => _client.OnReceived(buffer, offset, size);
}
