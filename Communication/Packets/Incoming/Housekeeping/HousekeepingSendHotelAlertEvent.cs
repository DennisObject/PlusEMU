using Plus.Communication.Attributes;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

[RequiresPermission(HousekeepingRights.Access)]
internal class HousekeepingSendHotelAlertEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IGameClientManager _clients;

    public HousekeepingSendHotelAlertEvent(IHousekeepingActionRunner runner, IGameClientManager clients)
    {
        _runner = runner;
        _clients = clients;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var message = HousekeepingLimits.Normalize(packet.ReadString());
        _runner.Run(session, "hotel.alert", HousekeepingRights.Alert, actor =>
        {
            if (message.Length == 0) {
                return HousekeepingOutcome.Fail(HousekeepingErrors.AlertEmpty, HousekeepingTarget.Hotel);
            }

            if (!HousekeepingLimits.IsText(message, HousekeepingLimits.MaxAlertLength)) {
                return HousekeepingOutcome.Invalid(HousekeepingTarget.Hotel);
            }

            _clients.SendPacket(new BroadcastMessageAlertComposer($"{message}\r\n- {actor.Username}"));

            return HousekeepingOutcome.Success(HousekeepingTarget.Hotel, $"reached={_clients.Count} message={HousekeepingLimits.AuditValue(message)}");
        });

        return Task.CompletedTask;
    }
}
