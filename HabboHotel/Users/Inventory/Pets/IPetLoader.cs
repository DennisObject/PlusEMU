using Plus.HabboHotel.Rooms.AI;

namespace Plus.HabboHotel.Users.Inventory.Pets;

internal interface IPetLoader
{
    List<Pet> GetPetsForUser(int userId);
}
