using Plus.Communication.Packets.Outgoing.Housekeeping;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Housekeeping;

namespace Plus.Communication.Packets.Incoming.Housekeeping;

internal class HousekeepingSearchRoomsEvent : IPacketEvent
{
    private readonly IHousekeepingActionRunner _runner;
    private readonly IHousekeepingRoomStore _rooms;

    public HousekeepingSearchRoomsEvent(IHousekeepingActionRunner runner, IHousekeepingRoomStore rooms)
    {
        _runner = runner;
        _rooms = rooms;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        if (!_runner.HasAccess(session)) return Task.CompletedTask;
        var query = HousekeepingLimits.Normalize(packet.ReadString());
        var exactMatch = packet.ReadBool();
        var limit = packet.ReadInt();
        var valid = query.Length > 0 && HousekeepingLimits.IsText(query, HousekeepingLimits.MaxLookupLength);
        session.Send(new HousekeepingRoomListComposer(valid ? _rooms.Search(query, exactMatch, limit) : Array.Empty<HousekeepingRoom>()));
        return Task.CompletedTask;
    }
}
