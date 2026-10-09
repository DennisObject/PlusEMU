using Plus.Communication.Packets.Outgoing.Rooms.Furni.RentableSpaces;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.RentableSpaces;

internal class GetRentableSpaceEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); // Rentable-space item ID; the current status response is not item-specific.
        session.Send(new RentableSpaceComposer());

        return Task.CompletedTask;
    }
}
