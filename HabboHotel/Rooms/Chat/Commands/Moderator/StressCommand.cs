using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

public class StressCommand : IChatCommand
{
    public string Key => "stress";
    public string Parameters => "bots <amount|clear>";
    public string Description => "Creates temporary walking and talking bots for room stress testing.";

    internal static bool CanExecute(UserAccess permissions) =>
        permissions.Can(PermissionKeys.ModerationTool) && permissions.Can("command.stress");

    internal static bool TryParse(string[] parameters, out int amount)
    {
        amount = 0;
        if (parameters.Length != 2 || !parameters[0].Equals("bots", StringComparison.OrdinalIgnoreCase))
            return false;
        if (parameters[1].Equals("clear", StringComparison.OrdinalIgnoreCase))
            return true;
        return int.TryParse(parameters[1], out amount) && amount > 0 && amount <= RoomUserManager.MaxStressBots;
    }

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!CanExecute(session.GetHabbo().Access))
        {
            session.SendWhisper("Only staff with moderator tools can use :stress.");
            return;
        }
        if (!TryParse(parameters, out var amount))
        {
            session.SendWhisper($"Usage: :stress bots <1-{RoomUserManager.MaxStressBots}> or :stress bots clear. Only bots are supported.");
            return;
        }
        if (!room.GetRoomUserManager().QueueStressBots(amount, session.GetHabbo().Id, message => session.SendWhisper(message)))
            session.SendWhisper("Stress request unavailable: the room is closing or its request queue is full. Try again shortly.");
    }
}
