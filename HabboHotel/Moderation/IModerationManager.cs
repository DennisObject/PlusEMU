namespace Plus.HabboHotel.Moderation;

public interface IModerationManager
{
    ICollection<string> UserMessagePresets { get; }
    ICollection<string> RoomMessagePresets { get; }
    ICollection<ModerationTicket> GetTickets { get; }
    Dictionary<string, List<ModerationPresetActions>> UserActionPresets { get; }
    void Init();
    void ReCacheBans();
    /// <summary>
    /// Writes the ban, then signs out every account it covers: covered sessions close at once, and under each account's
    /// session gate, while this ban is still in force, its credentials are revoked and any session that attached meanwhile
    /// closes. Work that outlives the deadline continues in the background.
    /// </summary>
    /// <param name="deadline">Shared by every step of one action; defaults to <see cref="ModerationManager.BanBudget"/>.</param>
    Task BanUser(string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp, CancellationToken deadline = default);

    /// <summary>
    /// Bans an account (and counts it), optionally with its recorded address and a device id, as one action on one deadline.
    /// <paramref name="heldUserId"/> names an account whose session gate the caller already holds.
    /// </summary>
    Task BanAccount(string mod, int userId, string username, string reason, double expireTimestamp, CancellationToken deadline = default,
        bool includeAddress = false, string? machineId = null, int heldUserId = 0);

    /// <summary>
    /// Removes a username ban from the database and cache. Returns false when no ban row existed.
    /// </summary>
    bool UnbanUser(string username);
    bool TryAddTicket(ModerationTicket ticket);
    bool TryGetTicket(int ticketId, out ModerationTicket ticket);
    bool UserHasTickets(int userId);
    ModerationTicket GetTicketBySenderId(int userId);

    /// <summary>
    /// Runs a quick check to see if a ban record is cached in the server.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="ban"></param>
    /// <returns></returns>
    bool IsBanned(string key, out ModerationBan ban);

    /// <summary>
    /// Run a quick database check to see if this ban exists in the database.
    /// </summary>
    /// <param name="machineId">The value of the ban.</param>
    /// <returns></returns>
    bool HasMachineBanCheck(string machineId);

    /// <summary>
    /// Run a quick database check to see if this ban exists in the database.
    /// </summary>
    /// <param name="username">The value of the ban.</param>
    /// <returns></returns>
    bool UsernameBanCheck(string username);

    /// <summary>
    /// Remove a ban from the cache based on a given value.
    /// </summary>
    /// <param name="value"></param>
    void RemoveBan(string value);
}