using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands.Moderator.Fun;

internal class FreezeCommand : ITargetChatCommand
{
    private readonly IGameClientManager _gameClientManager;
    public string Key => "freeze";

    public string Parameters => "%username%";

    public string Description => "Prevent another user from walking.";

    public bool MustBeInSameRoom => true;

    public FreezeCommand(IGameClientManager gameClientManager)
    {
        _gameClientManager = gameClientManager;
    }

    public Task Execute(GameClient session, Room room, Habbo target, string[] parameters)
    {
        if (!session.GetHabbo().Access.Outranks(target.Access))
        {
            return Task.CompletedTask;
        }

        var targetUser = session.GetHabbo().CurrentRoom.GetRoomUserManager().GetRoomUserByHabbo(target.Id);

        if (targetUser != null)
        {
            targetUser.Frozen = true;
        }

        session.SendWhisper($"Successfully froze {target.Username}!");

        return Task.CompletedTask;
    }
}
