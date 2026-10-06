using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class UnignoreUserEvent : IPacketEvent
{
    private readonly IGameClientManager _gameClientManager;

    public UnignoreUserEvent(IGameClientManager gameClientManager)
    {
        _gameClientManager = gameClientManager;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!session.GetHabbo().InRoom)
            return Task.CompletedTask;
        var room = session.GetHabbo().CurrentRoom;
        if (room == null)
            return Task.CompletedTask;
        var username = packet.ReadString();
        var player = _gameClientManager.GetClientByUsername(username)?.GetHabbo();
        if (player == null)
            return Task.CompletedTask;
        session.GetHabbo().IgnoresComponent.Unignore(player.Id);
        return Task.CompletedTask;
    }
}