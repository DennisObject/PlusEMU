using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Moderation;

public interface ITradeModerationService
{
    Task Lock(GameClient actor, int userId, int minutes, string reason);
}

public sealed class TradeModerationService(IUserDataFactory users, IGameClientManager clients, IAccessControl access, ITradingLockService tradingLocks) : ITradeModerationService
{
    public async Task Lock(GameClient actor, int userId, int minutes, string reason)
    {
        if (!await users.HabboExists(userId)) {
            actor.SendWhisper("An error occurred whilst finding that user in the database.");

            return;
        }

        if (!access.Outranks(actor.GetHabbo().Id, userId)) {
            actor.SendWhisper("Oops, you cannot trade lock another user with an equal or higher rank.");

            return;
        }

        var days = Math.Clamp(minutes / 1440.0, 1, 365);
        tradingLocks.Set(userId, TimeSpan.FromDays(days));
        clients.GetClientByUserId(userId)?.SendNotification($"You have been trade banned for {days} day(s)!\r\rReason:\r\r{reason}");
    }
}
