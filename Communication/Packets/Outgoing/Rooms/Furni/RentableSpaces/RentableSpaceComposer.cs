using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Rooms.Furni.RentableSpaces;

public class RentableSpaceComposer : IServerPacket
{
    public uint MessageId => ServerPacketHeader.RentableSpaceComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(true); //Is rented y/n
        packet.WriteInteger(-1); // Rental eligibility error code; zero means renting is allowed.
        packet.WriteInteger(-1); // Renter user ID.
        packet.WriteString(""); //Username of who owns.
        packet.WriteInteger(360); //Time to expire.
        packet.WriteInteger(-1); // Rental price.
    }
}
