using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingFindUserByNameEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingUserStore _users;
    private readonly IHousekeepingLookups _lookups;

    public HousekeepingFindUserByNameEvent(IHousekeepingActionRunner runner, IHousekeepingUserStore users, IHousekeepingLookups lookups)
    {
        _runner = runner;
        _users = users;
        _lookups = lookups;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!_runner.HasAccess(session)) return Task.CompletedTask;
        var username = HousekeepingLimits.Normalize(packet.ReadString());
        var record = HousekeepingLimits.IsText(username, HousekeepingLimits.MaxLookupLength) ? _users.Find(username) : null;
        session.Send(new HousekeepingUserDetailComposer(_lookups.User(session.GetHabbo(), record)));
        return Task.CompletedTask;
    }
}
