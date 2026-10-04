using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class KickCommand : ITargetChatCommand
{
    public string Key => "kick";

    public string Parameters => "%username% %reason%";

    public string Description => "Kick a user from a room and send them a reason.";

    public bool MustBeInSameRoom => false;
    
    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access))
            return Task.CompletedTask;
        if (target == session.GetHabbo())
        {
            session.SendWhisper("Get a life.");
            return Task.CompletedTask;
        }
        if (!target.InRoom)
        {
            session.SendWhisper("That user currently isn't in a room.");
            return Task.CompletedTask;
        }
        if (parameters.Any())
            target.Client.SendNotification($"A moderator has kicked you from the room for the following reason: {CommandManager.MergeParams(parameters)}");
        else
            target.Client.SendNotification("A moderator has kicked you from the room.");
        target.CurrentRoom.GetRoomUserManager().RemoveUserFromRoom(target.Client, true);
        return Task.CompletedTask;
    }
}