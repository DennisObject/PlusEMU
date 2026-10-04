using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Subscriptions;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class CanCreateRoomEvent(ISettingsManager settings) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var limit = ClubLimits.For(session.GetHabbo().Access, "rooms", settings);
        session.Send(new CanCreateRoomComposer(RoomFactory.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id).Count >= limit, limit));
        return Task.CompletedTask;
    }
}