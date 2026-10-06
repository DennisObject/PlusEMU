using Plus.Communication.Attributes;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingGiveCurrencyEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingEconomyActions _economy;

    public HousekeepingGiveCurrencyEvent(IHousekeepingActionRunner runner, IHousekeepingEconomyActions economy)
    {
        _runner = runner;
        _economy = economy;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var userId = packet.ReadInt();
        var currencyType = packet.ReadInt();
        var amount = packet.ReadInt();
        // Only the activity point types the client offers; credits have their own packet.
        _runner.Run(session, $"user.give_currency_{currencyType}", HousekeepingRights.Economy, actor =>
            currencyType is (int)HousekeepingCurrency.Duckets or (int)HousekeepingCurrency.Diamonds
                ? _economy.Give(actor, userId, (HousekeepingCurrency)currencyType, amount)
                : HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0))));

        return Task.CompletedTask;
    }
}
