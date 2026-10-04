using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingBanUserEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingBanUserEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var reason = packet.ReadString();
        var hours = packet.ReadInt();
        await _runner.RunAsync(session, "user.ban", HousekeepingRights.Sanction, actor => _users.Ban(actor, userId, reason, hours));
    }
}
