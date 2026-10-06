using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Game.Arena;

internal class Game2GameChatEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet) => throw new NotImplementedException();
}
