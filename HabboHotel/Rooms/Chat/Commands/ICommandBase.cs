using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms.Chat.Commands;

[Singleton]
public interface ICommandBase
{
    string Key { get; }
    string Parameters { get; }
    string Description { get; }
}