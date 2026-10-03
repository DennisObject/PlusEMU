namespace Plus.HabboHotel.Housekeeping;

/// <summary>
/// Rank rights (permissions table) that gate the in-client housekeeping panel.
/// Access opens the panel and its read-only lookups; every mutation also needs its own right.
/// </summary>
public static class HousekeepingRights
{
    public const string Access = "acc_housekeeping";
    public const string Sanction = "housekeeping_sanction";
    public const string Rank = "housekeeping_rank";
    public const string Password = "housekeeping_password";
    public const string Rooms = "housekeeping_rooms";
    public const string RoomOwnership = "housekeeping_room_ownership";
    public const string Economy = "housekeeping_economy";
    public const string Alert = "housekeeping_alert";
    public const string PrivateData = "housekeeping_private_data";
}
