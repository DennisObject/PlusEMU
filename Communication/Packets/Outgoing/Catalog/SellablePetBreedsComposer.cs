using Plus.HabboHotel.Catalog;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Catalog;

public sealed class SellablePetBreedsComposer(PetPaletteSnapshot snapshot) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.SellablePetBreedsComposer;

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(snapshot.Type);
        packet.WriteInteger(snapshot.Races.Length);

        foreach (var race in snapshot.Races)
        {
            packet.WriteInteger(snapshot.PetId);
            packet.WriteInteger(race.Breed);
            packet.WriteInteger(race.Palette);
            packet.WriteBoolean(true);
            packet.WriteBoolean(false);
            packet.WriteBoolean(false);
        }
    }
}
