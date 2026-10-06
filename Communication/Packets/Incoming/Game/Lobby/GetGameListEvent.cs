using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Games;

namespace Plus.Communication.Packets.Incoming.Game.Lobby;

internal class GetGameListEvent(IGameLobbyService service) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        service.ShowGames(session);

        return Task.CompletedTask;
    }
}
