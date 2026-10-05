using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator;

internal class GotoCommand(IRoomDataLoader rooms) : IChatCommand
{
    public string Key => "goto";

    public string Parameters => "%room_id%";

    public string Description => "";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        if (!parameters.Any())
        {
            session.SendWhisper("You must specify a room id!");
            return;
        }
        if (!uint.TryParse(parameters[0], out var roomId))
            session.SendWhisper("You must enter a valid room ID");
        else
        {
            if (!rooms.TryGetData(roomId, out _))
            {
                session.SendWhisper("This room does not exist!");
                return;
            }
            session.GetHabbo().PrepareRoom(roomId, "");
        }
    }
}