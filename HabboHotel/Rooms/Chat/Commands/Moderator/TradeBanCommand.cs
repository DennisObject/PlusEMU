using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class TradeBanCommand(ITradingLockService tradingLocks) : ITargetChatCommand
{
    public string Key => "tradeban";
    public string Parameters => "%target% %length%";
    public string Description => "Trade ban another user.";
    public bool MustBeInSameRoom => false;

    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access)) {
            return Task.CompletedTask;
        }

        if (parameters.Length == 0 || !double.TryParse(parameters[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var days) || !double.IsFinite(days)) {
            session.SendWhisper("Please enter the number of days. Use 0 to reset.");

            return Task.CompletedTask;
        }

        if (days == 0) {
            tradingLocks.Clear(target.Id);
            target.TradingLockExpiresAt = null;
            target.Client?.SendNotification("Your outstanding trade ban has been removed.");
            session.SendWhisper($"You have successfully removed {target.Username}'s trade ban.");
        }
        else {
            days = Math.Clamp(days, 1, 365);
            target.TradingLockExpiresAt = tradingLocks.Set(target.Id, TimeSpan.FromDays(days));
            target.Client?.SendNotification($"You have been trade banned for {days} day(s)!");
            session.SendWhisper($"You have successfully trade banned {target.Username} for {days} day(s).");
        }

        return Task.CompletedTask;
    }
}
