using Plus.Communication.Flash;
using Plus.HabboHotel.GameClients;

namespace Plus.Tests;

internal sealed class TestGameServer : IGameServer
{
    public static TestGameServer Instance { get; } = new();
    public bool Start() => true;
    public bool Stop() => true;
    public Task PacketReceived(GameClient client, uint messageId, IIncomingPacket packet) => Task.CompletedTask;
    public bool ModifyOutgoingPacket(GameClient client, IOutgoingPacket packet) => true;
    public bool HasOutgoingPacketInjectors(uint messageId) => false;
}
