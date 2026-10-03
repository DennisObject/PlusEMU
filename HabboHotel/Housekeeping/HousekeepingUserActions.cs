using System.Security.Cryptography;
using Dapper;
using Plus.Core.Settings;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Moderation;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Permissions;
using Plus.Utilities;
using static Plus.HabboHotel.Housekeeping.HousekeepingErrors;
using static Plus.HabboHotel.Housekeeping.HousekeepingUserTargets;

namespace Plus.HabboHotel.Housekeeping;

public interface IHousekeepingUserActions
{
    HousekeepingOutcome Ban(Habbo actor, int userId, string reason, int hours);
    HousekeepingOutcome Unban(Habbo actor, int userId);
    HousekeepingOutcome Mute(Habbo actor, int userId, string reason, int minutes);
    HousekeepingOutcome Kick(Habbo actor, int userId, string reason);
    HousekeepingOutcome Disconnect(Habbo actor, int userId, string reason);
    HousekeepingOutcome SetRank(Habbo actor, int userId, int rankId);
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
    private readonly IPermissionManager _permissions;
    private readonly ISettingsManager _settings;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDatabase _database;
    // Held for every write to an account so it cannot interleave with that account's login.
    private readonly IAccountSessionGate _sessionGate;

    public HousekeepingUserActions(IHousekeepingUserStore users, IGameClientManager clients, IModerationManager moderation, IPermissionManager permissions,
        ISettingsManager settings, IPasswordHasher passwordHasher, IDatabase database, IAccountSessionGate sessionGate)
    {
        _users = users;
        _clients = clients;
        _moderation = moderation;
        _permissions = permissions;
        _settings = settings;
        _passwordHasher = passwordHasher;
        _database = database;
        _sessionGate = sessionGate;
    }

    public HousekeepingOutcome Ban(Habbo actor, int userId, string reason, int hours)
    {
        reason = HousekeepingLimits.Normalize(reason);
        if (!HousekeepingLimits.InRange(hours, 1, HousekeepingLimits.MaxBanHours) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength))
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        var expire = UnixTimestamp.GetNow() + hours * 3600.0;
        _moderation.BanUser(actor.Username, ModerationBanType.Username, user.Username, reason.Length > 0 ? reason : "No reason specified.", expire);
        Execute("UPDATE `user_info` SET `bans` = `bans` + 1 WHERE `user_id` = @userId", new { userId });
        _clients.GetClientByUserId(userId)?.Disconnect();
        return HousekeepingOutcome.Success(Label(user), $"hours={hours} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Unban(Habbo actor, int userId)
    {
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        return _moderation.UnbanUser(user.Username)
            ? HousekeepingOutcome.Success(Label(user), "unbanned")
            : HousekeepingOutcome.Fail(NoActiveBan, Label(user));
    }

    public HousekeepingOutcome Mute(Habbo actor, int userId, string reason, int minutes)
    {
        reason = HousekeepingLimits.Normalize(reason);
        if (!HousekeepingLimits.InRange(minutes, 1, HousekeepingLimits.MaxMuteMinutes) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength))
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        var seconds = minutes * 60.0;
        Execute("UPDATE `users` SET `time_muted` = @seconds WHERE `id` = @userId LIMIT 1", new { seconds, userId });
        if (_clients.Online(userId) is { } client)
        {
            client.GetHabbo().TimeMuted = seconds;
            client.SendNotification(reason.Length > 0 ? reason : $"You have been muted by a moderator for {minutes} minute(s)!");
        }
        return HousekeepingOutcome.Success(Label(user), $"minutes={minutes} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Kick(Habbo actor, int userId, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);
        if (!HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        if (_clients.Online(userId) is not { } client) return HousekeepingOutcome.Fail(UserOffline, Label(user));
        var room = client.GetHabbo().CurrentRoom;
        if (room == null) return HousekeepingOutcome.Fail(UserNotInRoom, Label(user));
        client.SendNotification(reason.Length > 0 ? $"A moderator has kicked you from the room for the following reason: {reason}" : "A moderator has kicked you from the room.");
        room.GetRoomUserManager().RemoveUserFromRoom(client, true);
        return HousekeepingOutcome.Success(Label(user), $"roomId={room.Id} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome Disconnect(Habbo actor, int userId, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);
        if (!HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength)) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        if (_clients.Online(userId) is not { } client) return HousekeepingOutcome.Fail(UserOffline, Label(user));
        if (reason.Length > 0) client.SendNotification(reason);
        client.Disconnect();
        return HousekeepingOutcome.Success(Label(user), $"reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome SetRank(Habbo actor, int userId, int rankId)
    {
        if (rankId <= 0) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        if (!_permissions.TryGetGroup(rankId, out _)) return HousekeepingOutcome.Fail(RankNotFound, HousekeepingTarget.User(userId), $"rankId={rankId}");
        if (!HousekeepingRankPolicy.CanAssign(actor.Rank, rankId)) return HousekeepingOutcome.Fail(RankTooHigh, HousekeepingTarget.User(userId), $"rankId={rankId}");
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        Execute("UPDATE `users` SET `rank` = @rankId WHERE `id` = @userId LIMIT 1", new { rankId, userId });
        if (_clients.Online(userId) is { } client)
        {
            // Rights are resolved at login; refresh them so a demotion takes effect immediately.
            var habbo = client.GetHabbo();
            habbo.Rank = rankId;
            habbo.Permissions = new(_permissions.GetPermissionsForPlayer(habbo), _permissions.GetCommandsForPlayer(habbo));
            client.Send(ClientPermissions.Composer(habbo, _permissions, _settings));
        }
        return HousekeepingOutcome.Success(Label(user), $"fromRank={user.Rank} toRank={rankId}");
    }

    public HousekeepingOutcome TradeLock(Habbo actor, int userId, int hours, string reason)
    {
        reason = HousekeepingLimits.Normalize(reason);
        if (!HousekeepingLimits.InRange(hours, 1, HousekeepingLimits.MaxTradeLockHours) || !HousekeepingLimits.IsText(reason, HousekeepingLimits.MaxReasonLength))
            return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        var until = UnixTimestamp.GetNow() + hours * 3600.0;
        Execute("INSERT INTO `user_info` (`user_id`, `trading_locked`, `trading_locks_count`) VALUES (@userId, @until, 1) " +
                "ON DUPLICATE KEY UPDATE `trading_locked` = @until, `trading_locks_count` = `trading_locks_count` + 1", new { userId, until });
        if (_clients.Online(userId) is { } client)
        {
            client.GetHabbo().TradingLockExpiry = until;
            client.SendNotification(reason.Length > 0 ? $"You have been trade banned for {hours} hour(s)!\r\rReason:\r\r{reason}" : $"You have been trade banned for {hours} hour(s)!");
        }
        return HousekeepingOutcome.Success(Label(user), $"hours={hours} reason={HousekeepingLimits.AuditValue(reason)}");
    }

    public HousekeepingOutcome ResetPassword(Habbo actor, int userId)
    {
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, out var user) is { } denied) return denied;
        var password = GeneratePassword();
        // The SSO ticket is cleared (login rejects empty tickets) and the live session closed so old credentials stop working at once.
        Execute("UPDATE `users` SET `password` = @hash, `auth_ticket` = '' WHERE `id` = @userId LIMIT 1", new { hash = _passwordHasher.Hash(password), userId });
        _sessionGate.Revoke(userId);
        _clients.GetClientByUserId(userId)?.Disconnect();
        // The plaintext only travels back to the acting operator; the audit detail never contains it.
        return HousekeepingOutcome.Success(Label(user), "password_reset", password);
    }

    internal static string GeneratePassword() => RandomNumberGenerator.GetString(PasswordAlphabet, PasswordLength);

    private void Execute(string sql, object parameters)
    {
        using var connection = _database.Connection();
        connection.Execute(sql, parameters);
    }
}
