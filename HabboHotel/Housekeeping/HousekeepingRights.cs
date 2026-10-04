using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Housekeeping;

/// <summary>
/// Permission keys that gate the in-client housekeeping panel.
/// Access opens the panel and its read-only lookups; every mutation also needs its own right.
/// </summary>
public static class HousekeepingRights
{
    public const string Access = PermissionKeys.HousekeepingAccess;
    public const string Sanction = PermissionKeys.HousekeepingSanction;
    public const string Password = PermissionKeys.HousekeepingPassword;
    public const string Rooms = PermissionKeys.HousekeepingRooms;
    public const string RoomOwnership = PermissionKeys.HousekeepingRoomOwnership;
    public const string Economy = PermissionKeys.HousekeepingEconomy;
    public const string Alert = PermissionKeys.HousekeepingAlert;
    public const string PrivateData = PermissionKeys.HousekeepingPrivateData;
}
