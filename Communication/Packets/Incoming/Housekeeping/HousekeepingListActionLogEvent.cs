using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingListActionLogEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingAuditLog _auditLog;

    public HousekeepingListActionLogEvent(IHousekeepingActionRunner runner, IHousekeepingAuditLog auditLog)
    {
        _runner = runner;
        _auditLog = auditLog;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!_runner.HasAccess(session)) return Task.CompletedTask;
        session.Send(new HousekeepingActionLogComposer(_auditLog.List(packet.ReadInt())));
        return Task.CompletedTask;
    }
}
