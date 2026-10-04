using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingKickUserEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingKickUserEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var reason = packet.ReadString();
        _runner.Run(session, "user.kick", HousekeepingRights.Sanction, actor => _users.Kick(actor, userId, reason));
        return Task.CompletedTask;
    }
}
