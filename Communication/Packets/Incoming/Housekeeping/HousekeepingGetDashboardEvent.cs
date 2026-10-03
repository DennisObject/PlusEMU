using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingGetDashboardEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingLookups _lookups;

    public HousekeepingGetDashboardEvent(IHousekeepingActionRunner runner, IHousekeepingLookups lookups)
    {
        _runner = runner;
        _lookups = lookups;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!_runner.HasAccess(session)) return Task.CompletedTask;
        session.Send(new HousekeepingDashboardComposer(_lookups.Dashboard()));
        return Task.CompletedTask;
    }
}
