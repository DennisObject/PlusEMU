using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Moderation;

public sealed record ModerationBanRequest(int UserId, string Message, int Hours, bool IpBan, bool MachineBan);

public interface IModerationSanctionService
{
    Task Ban(GameClient actor, ModerationBanRequest request);
}

internal static class ModerationBanDuration
{
    public static bool TryGetExpiry(DateTimeOffset now, double hours, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (!double.IsFinite(hours) || hours <= 0)
            return false;

        TimeSpan delay;
        try
        {
            delay = TimeSpan.FromHours(hours);
        }
        catch (OverflowException)
        {
            return false;
        }
        return TryGetExpiry(now, delay, out expiresAt);
    }

    public static bool TryGetExpiry(DateTimeOffset now, TimeSpan delay, out DateTimeOffset expiresAt)
    {
        expiresAt = default;
        if (delay <= TimeSpan.Zero || delay > DateTimeOffset.MaxValue - now)
            return false;
        expiresAt = now + delay;
        return true;
    }
}

public sealed class ModerationSanctionService(
    IGameClientManager clients,
    IModeratorUserLookup users,
    IModerationManager moderation,
    TimeProvider clock) : IModerationSanctionService
{
    public async Task Ban(GameClient actor, ModerationBanRequest request)
    {
        var moderator = actor.GetHabbo();
        if (request.IpBan && !moderator.Access.Can(PermissionKeys.ModerationIpBan) ||
            request.MachineBan && !moderator.Access.Can(PermissionKeys.ModerationMachineBan))
            return;

        var now = clock.GetUtcNow();
        if (!ModerationBanDuration.TryGetExpiry(now, request.Hours, out var expiresAt))
            return;

        using var deadline = new CancellationTokenSource(ModerationManager.BanBudget);
        var targetClient = clients.GetClientByUserId(request.UserId);
        var target = targetClient?.GetHabbo() ?? users.GetById(request.UserId);
        if (target == null)
        {
            actor.SendWhisper("An error occoured whilst finding that user in the database.");
            return;
        }
        if (!moderator.Access.Outranks(target.Access))
        {
            actor.SendWhisper("Oops, you cannot ban that user.");
            return;
        }

        string? machineId = null;
        if (request.MachineBan && targetClient != null)
        {
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
            machineId = targetClient.MachineId;
#pragma warning restore CS0618
        }

        // Fail closed first for a live mod-tool target. The coordinator keeps the account, address and device
        // revocation under this action's one deadline and preserves its retry ordering.
        targetClient?.Disconnect();
        await moderation.BanAccount(moderator.Username, target.Id, target.Username,
            request.Message ?? "No reason specified.", expiresAt, deadline.Token,
            includeAddress: request.IpBan || request.MachineBan,
            machineId: machineId);
    }
}
