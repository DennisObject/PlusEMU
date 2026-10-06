using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingGrantItemEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingEconomyActions _economy;

    public HousekeepingGrantItemEvent(IHousekeepingActionRunner runner, IHousekeepingEconomyActions economy)
    {
        _runner = runner;
        _economy = economy;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var itemId = packet.ReadInt();
        var quantity = packet.ReadInt();
        _runner.Run(session, "user.grant_item", HousekeepingRights.Economy, actor => _economy.GrantItem(actor, userId, itemId, quantity));

        return Task.CompletedTask;
    }
}
