namespace Plus.HabboHotel.Users.Authentication;

/// <summary>
/// Game login tickets kept in users.auth_ticket. A ticket is random, expires after
/// AuthApi:SsoTicketLifetimeSeconds, matches case-exactly and can log in once.
/// </summary>
public interface ISsoTicketStore
{
    /// <summary>Replaces the user's ticket with a fresh one.</summary>
    Task<IssuedToken> Issue(int userId);

    /// <summary>The owner of a live ticket, without using it up.</summary>
    Task<int?> FindUser(string ticket);

    /// <summary>Uses up a live ticket. Of several concurrent callers only one gets the user id.</summary>
    Task<int?> Consume(string ticket);

    /// <summary>
    /// Marks a live ticket as traded for an access token. Each ticket can be exchanged once; the
    /// game login can still redeem it afterwards.
    /// </summary>
    Task<int?> Exchange(string ticket);

    /// <summary>Clears whatever ticket the user still holds.</summary>
    Task Revoke(int userId);
}
