using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingSetHcSubscriptionEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingEconomyActions _economy;

    public HousekeepingSetHcSubscriptionEvent(IHousekeepingActionRunner runner, IHousekeepingEconomyActions economy)
    {
        _runner = runner;
        _economy = economy;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var days = packet.ReadInt();
        _runner.Run(session, "user.set_hc", HousekeepingRights.Economy, actor => _economy.SetClub(actor, userId, days));
        return Task.CompletedTask;
    }
}
