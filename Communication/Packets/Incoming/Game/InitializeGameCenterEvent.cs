using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game;

internal class InitializeGameCenterEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => Task.CompletedTask;
}
