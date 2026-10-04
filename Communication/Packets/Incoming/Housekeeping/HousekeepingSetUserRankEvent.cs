using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingSetUserRankEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserActions _users;

    public HousekeepingSetUserRankEvent(IHousekeepingActionRunner runner, IHousekeepingUserActions users)
    {
        _runner = runner;
        _users = users;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var rankId = packet.ReadInt();
        _runner.Run(session, "user.set_rank", HousekeepingRights.Rank, actor => _users.SetRank(actor, userId, rankId));
        return Task.CompletedTask;
    }
}
