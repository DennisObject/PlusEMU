using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Action;

internal class IgnoreUserEvent : IPacketEvent
{
    private readonly IAchievementManager _achievementManager;
    private readonly IGameClientManager _gameClientManager;

    public IgnoreUserEvent(IAchievementManager achievementManager, IGameClientManager gameClientManager)
    {
        _achievementManager = achievementManager;
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
        if (player == null || player.Access.Can(PermissionKeys.ModerationTool))
            return Task.CompletedTask;
        if (session.GetHabbo().IgnoresComponent.IsIgnored(player.Id))
            return Task.CompletedTask;
        session.GetHabbo().IgnoresComponent.Ignore(player.Id);
        _achievementManager.ProgressAchievement(session, "ACH_SelfModIgnoreSeen", 1);
        return Task.CompletedTask;
    }
}