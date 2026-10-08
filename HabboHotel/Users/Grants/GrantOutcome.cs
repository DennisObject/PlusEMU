namespace Plus.HabboHotel.Users.Grants;

/// <summary>
/// Result of an external grant. <see cref="Code"/> is a stable machine-readable id that CMSes branch on; the detail is
/// human-readable only. A result object is present only on success.
/// </summary>
public sealed record GrantOutcome(string Code, object? Result = null, string? Detail = null)
{
    public const string Ok = "ok";
    public const string AlreadyApplied = "already_applied";
    public const string UserOnline = "user_online";
    public const string UserNotFound = "user_not_found";
    public const string InvalidPayload = "invalid_payload";
    public const string InsufficientBalance = "insufficient_balance";
    public const string BalanceOverflow = "balance_overflow";
    public const string UnknownFurniture = "unknown_furniture";
    public const string FurnitureNotGrantable = "furniture_not_grantable";
    public const string UnknownBadge = "unknown_badge";
    public const string RestrictedBadge = "restricted_badge";
    public const string InvalidRank = "invalid_rank";
    public const string UnknownRoom = "unknown_room";
    public const string KeyPayloadMismatch = "key_payload_mismatch";
    public const string TooLarge = "too_large";
    public const string Busy = "busy";
    public const string InternalError = "internal_error";

    public bool Succeeded => Code is Ok or AlreadyApplied;

    public static GrantOutcome Success(object result) => new(Ok, result);

    public static GrantOutcome Fail(string code, string? detail = null) => new(code, null, detail);

    /// <summary>Final balance check shared by every grant: refuse below 0 and beyond the int wire range.</summary>
    public static string? CheckBalance(long balance) => balance switch
    {
        < 0 => InsufficientBalance,
        > int.MaxValue => BalanceOverflow,
        _ => null,
    };
}
