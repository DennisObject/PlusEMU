using System.Security.Cryptography;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities;
using static Plus.HabboHotel.Housekeeping.HousekeepingErrors;
using static Plus.HabboHotel.Housekeeping.HousekeepingUserTargets;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingUserActions
{
    Task<HousekeepingOutcome> Ban(Habbo actor, int userId, string reason, int hours);
    HousekeepingOutcome Unban(Habbo actor, int userId);
    HousekeepingOutcome Mute(Habbo actor, int userId, string reason, int minutes);
    HousekeepingOutcome Kick(Habbo actor, int userId, string reason);
    HousekeepingOutcome Disconnect(Habbo actor, int userId, string reason);
    HousekeepingOutcome TradeLock(Habbo actor, int userId, int hours, string reason);
    HousekeepingOutcome ResetPassword(Habbo actor, int userId);
}

public sealed class HousekeepingUserActions : IHousekeepingUserActions
{
    // No 0/O, 1/l/I so the operator can read it out without mistakes.
    private const string PasswordAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
    private const int PasswordLength = 16;

    private readonly IHousekeepingUserStore _users;
    private readonly IGameClientManager _clients;
    private readonly IModerationManager _moderation;
    private readonly IAccessControl _permissions;
    private readonly IBoundedPasswordHasher _passwordHasher;
    private readonly ISessionIssuer _sessions;
    private readonly IDatabase _database;
    private readonly ITradingLockService _tradingLocks;
    // Held for every write to an account so it cannot interleave with that account's login.
    private readonly IAccountSessionGate _sessionGate;
    private readonly TimeProvider _clock;

    public HousekeepingUserActions(IHousekeepingUserStore users, IGameClientManager clients, IModerationManager moderation, IAccessControl permissions,
        IBoundedPasswordHasher passwordHasher, IDatabase database, IAccountSessionGate sessionGate,
        ISessionIssuer sessions, ITradingLockService tradingLocks, TimeProvider clock)
    {
        _users = users;
        _clients = clients;
        _moderation = moderation;
        _permissions = permissions;
        _passwordHasher = passwordHasher;
        _database = database;
        _sessionGate = sessionGate;
        _sessions = sessions;
        _tradingLocks = tradingLocks;
        _clock = clock;
    }

    public async Task<HousekeepingOutcome> Ban(Habbo actor, int userId, string reason, int hours)
    {
        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        reason = HousekeepingLimits.Normalize(reason);

        if (!HousekeepingLimits.InRange(hours, 1, HousekeepingLimits.MaxBanHours) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) {
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        }

        using var account = await _sessionGate.EnterAsync(userId, deadline.Token);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        var expiresAt = _clock.GetUtcNow().AddHours(hours);
        // The ban coordinator counts the ban, signs the account out and closes its session; this action holds the gate.
        await _moderation.BanAccount(actor.Username, userId, user.Username, reason.Length > 0 ? reason : "No reason specified.", expiresAt, deadline.Token,
            heldUserId: userId);

        return HousekeepingOutcome.Success(Label(user), $"hours={hours} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Unban(Habbo actor, int userId)
    {
        using var account = _sessionGate.Enter(userId);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        return _moderation.UnbanUser(user.Username)
            ? HousekeepingOutcome.Success(Label(user), "unbanned")
            : HousekeepingOutcome.Fail(NoActiveBan, Label(user));
    }

    public HousekeepingOutcome Mute(Habbo actor, int userId, string reason, int minutes)
    {
        reason = HousekeepingLimits.Normalize(reason);

        if (!HousekeepingLimits.InRange(minutes, 1, HousekeepingLimits.MaxMuteMinutes) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) {
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        }

        using var account = _sessionGate.Enter(userId);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        var seconds = minutes * 60.0;
        Execute("UPDATE `users` SET `time_muted` = @seconds WHERE `id` = @userId LIMIT 1", new { seconds, userId });

        if (_clients.Online(userId) is { } client) {
            client.GetHabbo().TimeMuted = seconds;
            client.SendNotification(reason.Length > 0 ? reason : $"You have been muted by a moderator for {minutes} minute(s)!");
        }

        return HousekeepingOutcome.Success(Label(user), $"minutes={minutes} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Kick(Habbo actor, int userId, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);

        if (!HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) {
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        }

        using var account = _sessionGate.Enter(userId);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        if (_clients.Online(userId) is not { } client) {
            return HousekeepingOutcome.Fail(UserOffline, Label(user));
        }

        var room = client.GetHabbo().CurrentRoom;

        if (room == null) {
            return HousekeepingOutcome.Fail(UserNotInRoom, Label(user));
        }

        client.SendNotification(reason.Length > 0 ? $"A moderator has kicked you from the room for the following reason: {reason}" : "A moderator has kicked you from the room.");
        room.GetRoomUserManager().RemoveUserFromRoom(client, true);

        return HousekeepingOutcome.Success(Label(user), $"roomId={room.Id} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Disconnect(Habbo actor, int userId, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);

        if (!HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) {
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        }

        using var account = _sessionGate.Enter(userId);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        if (_clients.Online(userId) is not { } client) {
            return HousekeepingOutcome.Fail(UserOffline, Label(user));
        }

        if (reason.Length > 0) {
            client.SendNotification(reason);
        }

        client.Disconnect();

        return HousekeepingOutcome.Success(Label(user), $"reason={HousekeepingLimits.AuditValue(reason)}");
    }


    public HousekeepingOutcome TradeLock(Habbo actor, int userId, int hours, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);

        if (!HousekeepingLimits.InRange(hours, 1, HousekeepingLimits.MaxTradeLockHours) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) {
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        }

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        _tradingLocks.Set(userId, TimeSpan.FromHours(hours));

        if (_clients.Online(userId) is { } client) {
            client.SendNotification(reason.Length > 0 ? $"You have been trade banned for {hours} hour(s)!\r\rReason:\r\r{reason}" : $"You have been trade banned for {hours} hour(s)!");
        }

        return HousekeepingOutcome.Success(Label(user), $"hours={hours} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome ResetPassword(Habbo actor, int userId)
    {
        using var account = _sessionGate.Enter(userId);

        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) {
            return denied;
        }

        var password = GeneratePassword();
        // The new hash is written before the revocation: a login that reads the row after the generation bump must
        // already find the new password, or it would pass with the old one under the new generation.
        Execute("UPDATE `users` SET `password` = @hash WHERE `id` = @userId LIMIT 1", new { hash = _passwordHasher.Hash(password).GetAwaiter().GetResult(), userId });
        SignOutEverywhere(userId);
        _clients.GetClientByUserId(userId)?.Disconnect();

        // The plaintext only travels back to the acting operator; the audit detail never contains it.
        return HousekeepingOutcome.Success(Label(user), "password_reset", password);
    }

    /// <summary>
    /// Revokes the game ticket, access tokens and remember tokens (bumping the credential generation), then stamps
    /// the account so a game login already past its ticket cannot finish. Callers hold the account's session gate.
    /// </summary>
    private void SignOutEverywhere(int userId)
    {
        // Housekeeping actions run synchronously on the packet thread; the emulator has no synchronization context.
        _sessions.RevokeAll(userId).GetAwaiter().GetResult();
        _sessionGate.Revoke(userId);
    }

    internal static string GeneratePassword() => RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);

    private void Execute(string sql, object parameters)
    {
        using var connection = _database.Connection();
        connection.Execute(sql, parameters);
    }
}
