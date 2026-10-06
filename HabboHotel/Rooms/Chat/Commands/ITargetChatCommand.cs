using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Chat.Commands;

public interface ITargetChatCommand : ICommandBase
{
    /// <summary>
    /// The target must be in the same room as the executing Habbo
    /// </summary>
    bool MustBeInSameRoom { get; }

    /// <summary>
    /// Execute the command.
    /// </summary>
    /// <param name="session"><see cref="GameClient"/> session executing the command.</param>
    /// <param name="room">The room <see cref="session"/> is in.</param>
    /// <param name="target">The target <see cref="Habbo"/>"/>.</param>
    /// <param name="parameters">Additional optional parameters. Does not include the command key and the username.</param>
    /// <returns></returns>
    Task Execute(GameClient session, Room room, Habbo target, string[] parameters);
}
