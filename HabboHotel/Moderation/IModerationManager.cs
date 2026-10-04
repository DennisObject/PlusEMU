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
    /// Writes the ban, then signs out every account it covers: the session closes at once, and under each account's
    /// session gate its credentials are revoked, the gate is stamped and any session that registered meanwhile closes.
    /// </summary>
    /// <param name="deadline">Shared by the bans of one compound action; defaults to <see cref="ModerationManager.BanBudget"/>.</param>
    Task BanUser(string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp, CancellationToken deadline = default);

    /// <summary>As <see cref="BanUser"/>, for a caller that already holds the session gate of <paramref name="heldUserId"/>.</summary>
    Task BanUserHoldingGate(int heldUserId, string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp);

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