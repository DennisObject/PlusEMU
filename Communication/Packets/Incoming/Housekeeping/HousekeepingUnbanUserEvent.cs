using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingUnbanUserEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingUnbanUserEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        _runner.Run(session, "user.unban", HousekeepingRights.Sanction, actor => _users.Unban(actor, userId));

        return Task.CompletedTask;
    }
}
