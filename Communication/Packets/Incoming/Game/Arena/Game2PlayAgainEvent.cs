using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

internal class Game2PlayAgainEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
