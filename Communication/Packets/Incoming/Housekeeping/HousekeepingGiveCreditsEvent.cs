using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingGiveCreditsEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingEconomyActions _economy;

    public HousekeepingGiveCreditsEvent(IHousekeepingActionRunner runner, IHousekeepingEconomyActions economy)
    {
        _runner = runner;
        _economy = economy;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var amount = packet.ReadInt();
        _runner.Run(session, "user.give_credits", HousekeepingRights.Economy, actor => _economy.Give(actor, userId, HousekeepingCurrency.Credits, amount));
        return Task.CompletedTask;
    }
}
