using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingForceDisconnectUserEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingForceDisconnectUserEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var reason = packet.ReadString();
        _runner.Run(session, "user.disconnect", HousekeepingRights.Sanction, actor => _users.Disconnect(actor, userId, reason));
        return Task.CompletedTask;
    }
}
