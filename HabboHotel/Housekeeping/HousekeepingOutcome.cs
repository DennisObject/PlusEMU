namespace Plus.HabboHotel.Housekeeping;

public enum HousekeepingTargetType
{
    User,
    Room,
    Hotel
}

public sealed record HousekeepingTarget(HousekeepingTargetType Type, int Id, string Label)
{
    public static readonly HousekeepingTarget Hotel = new(HousekeepingTargetType.Hotel, 0, string.Empty);

    public static HousekeepingTarget User(int id, string label = "") => new(HousekeepingTargetType.User, id, label);

    public static HousekeepingTarget Room(int id, string label = "") => new(HousekeepingTargetType.Room, id, label);
}

/// <summary>
/// Result of one staff mutation. Message goes to the operator only (an error key, or the one-time
/// password for a reset); Detail goes to the audit log and must never contain secrets.
/// </summary>
public sealed record HousekeepingOutcome(bool Ok, int ActionId, string Message, HousekeepingTarget Target, string Detail)
{
    public static HousekeepingOutcome Success(HousekeepingTarget target, string detail, string message = "") =>
        new(true, target.Id, message, target, detail);

    public static HousekeepingOutcome Invalid(HousekeepingTarget target) => Fail(HousekeepingErrors.InvalidInput, target);

    public static HousekeepingOutcome Fail(string error, HousekeepingTarget target, string detail = "") =>
        new(false, 0, error, target, string.IsNullOrEmpty(detail) ? error : $"{error} {detail}");
}

public static class HousekeepingErrors
{
    public const string InvalidInput = "housekeeping.error.invalid_input";
    public const string Forbidden = "housekeeping.error.forbidden";
    public const string RankTooHigh = "housekeeping.error.rank_too_high";
    public const string UserNotFound = "housekeeping.error.user_not_found";
    public const string UserOffline = "housekeeping.error.user_offline";
    public const string UserNotInRoom = "housekeeping.error.user_not_in_room";
    public const string NoActiveBan = "housekeeping.error.no_active_ban";
    public const string RoomNotFound = "housekeeping.error.room_not_found";
    public const string RoomNotLoaded = "housekeeping.error.room_not_loaded";
    public const string RoomActionFailed = "housekeeping.error.room_action_failed";
    public const string NewOwnerNotFound = "housekeeping.error.new_owner_not_found";
    public const string ItemNotFound = "housekeeping.error.item_not_found";
    public const string EconomyFailed = "housekeeping.error.economy_failed";
    public const string BalanceOverflow = "housekeeping.error.balance_overflow";
    public const string AlertEmpty = "housekeeping.error.alert_empty";
    public const string HashFailed = "housekeeping.error.hash_failed";
}
