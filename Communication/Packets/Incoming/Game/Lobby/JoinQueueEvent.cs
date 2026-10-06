using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

internal class JoinQueueEvent(IGameLobbyService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.JoinQueue(session, packet.ReadInt());
        return Task.CompletedTask;
    }
}