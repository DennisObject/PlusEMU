using System.Collections.Immutable;
using System.Globalization;

namespace Plus.HabboHotel.Rooms.AI;

public sealed record PetInventoryEntry(int Id, string Name, int Type, int Race, string Color, ImmutableArray<int> CustomParts, int Level);
public sealed record PetInventorySnapshot(ImmutableArray<PetInventoryEntry> Pets);
public sealed record HorseAppearanceSnapshot(int VirtualId, int PetId, int Type, int Race, string Color, int Hair, int HairDye, int Saddle, bool Riding);

public static class PetAppearanceSnapshots
{
    public static PetInventorySnapshot Inventory(IEnumerable<Pet> pets) => new(pets.Select(pet => new PetInventoryEntry(
        pet.PetId, pet.Name, pet.Type, int.Parse(pet.Race, CultureInfo.InvariantCulture), pet.Color,
        pet.CustomParts.Split(' ').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToImmutableArray(), pet.Level)).ToImmutableArray());

    public static HorseAppearanceSnapshot Horse(RoomUser user)
    {
        var pet = user.PetData;
        return new(pet.VirtualId, pet.PetId, pet.Type, int.Parse(pet.Race, CultureInfo.InvariantCulture),
            pet.Color.ToLowerInvariant(), pet.PetHair, pet.HairDye, pet.Saddle, user.RidingHorse);
    }
}
