using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Communication.Packets.Outgoing.Inventory.Pets;

public sealed class PetInventoryComposer(PetInventorySnapshot data) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.PetInventoryComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(1);
        packet.WriteInteger(0);
        packet.WriteInteger(data.Pets.Length);

        foreach (var pet in data.Pets) {
            packet.WriteInteger(pet.Id);
            packet.WriteString(pet.Name);
            packet.WriteInteger(pet.Type);
            packet.WriteInteger(pet.Race);
            packet.WriteString(pet.Color);
            packet.WriteInteger(0);

            foreach (var part in pet.CustomParts) {
                packet.WriteInteger(part);
            }

            packet.WriteInteger(pet.Level);
        }
    }
}
