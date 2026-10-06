using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal sealed class GetGuestRoomEvent(IGuestRoomInfoService guestRooms) : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var roomId = packet.ReadUInt();
        var isLoading = packet.ReadInt() == 1;
        var checkEntry = packet.ReadInt() == 1;
        var snapshot = guestRooms.Capture(roomId, session.GetHabbo(), isLoading, checkEntry);

        if (snapshot != null) {
            session.Send(new GetGuestRoomResultComposer(snapshot));
        }

        return Task.CompletedTask;
    }
}
