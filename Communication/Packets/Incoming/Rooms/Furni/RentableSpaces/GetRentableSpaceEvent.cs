using Plus.Communication.Packets.Outgoing.Rooms.Furni.RentableSpaces;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Incoming.Rooms.Furni.RentableSpaces;

internal class GetRentableSpaceEvent : IPacketEvent
{
    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        packet.ReadInt(); //unknown
        session.Send(new RentableSpaceComposer());

        return Task.CompletedTask;
    }
}
