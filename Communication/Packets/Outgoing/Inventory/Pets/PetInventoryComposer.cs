using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Outgoing.Inventory.Pets;

public class PetInventoryComposer : IServerPacket
{
    private readonly ICollection<Pet> _pets;
    public uint MessageId => ServerPacketHeader.PetInventoryComposer;

    public PetInventoryComposer(ICollection<Pet> pets)
    {
        _pets = pets;
    }

    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(0);
        packet.WriteInteger(_pets.Count);
        foreach (var pet in _pets.ToList())
        {
            packet.WriteInteger(pet.PetId);
            packet.WriteString(pet.Name);
            packet.WriteInteger(pet.Type);
            packet.WriteInteger(int.Parse(pet.Race));
            packet.WriteString(pet.Color);
            packet.WriteInteger(0); // Breed id.
            foreach (var part in pet.CustomParts.Split(' '))
                packet.WriteInteger(int.Parse(part));
            packet.WriteInteger(pet.Level);
        }
    }
}