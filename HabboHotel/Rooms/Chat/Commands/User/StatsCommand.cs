using System.Text;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal class StatsCommand : IChatCommand
{
    public string Key => "stats";

    public string Parameters => "";

    public string Description => "View your current statistics.";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var minutes = session.GetHabbo().HabboStats.OnlineTime / 60;
        var hours = minutes / 60;
        var onlineTime = Convert.ToInt32(hours);
        var s = onlineTime == 1 ? "" : "s";
        var habboInfo = new StringBuilder();
        habboInfo.Append("Your account stats:\r\r");
        habboInfo.Append("Currency Info:\r");
        habboInfo.Append($"Credits: {session.GetHabbo().Credits}\r");
        habboInfo.Append($"Duckets: {session.GetHabbo().Duckets}\r");
        habboInfo.Append($"Diamonds: {session.GetHabbo().Diamonds}\r");
        habboInfo.Append($"Online Time: {onlineTime} Hour{s}\r");
        habboInfo.Append($"Respects: {session.GetHabbo().HabboStats.Respect}\r");
        habboInfo.Append($"GOTW Points: {session.GetHabbo().GotwPoints}\r\r");
        session.SendNotification(habboInfo.ToString());
    }
}