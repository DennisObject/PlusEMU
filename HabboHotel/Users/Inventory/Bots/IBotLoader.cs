namespace Plus.HabboHotel.Users.Inventory.Bots;

internal interface IBotLoader
{
    List<Bot> GetBotsForUser(int userId);
}
