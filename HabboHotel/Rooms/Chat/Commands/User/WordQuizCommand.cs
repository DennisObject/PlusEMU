using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Polls;

namespace Plus.HabboHotel.Rooms.Chat.Commands.User;

internal sealed class WordQuizCommand(IRoomWordQuizService quizzes) : IChatCommand
{
    public string Key => "wordquiz";
    public string Parameters => "[seconds] question";
    public string Description => "Start a yes/no question in your room (1–300 seconds, default 60).";

    public void Execute(GameClient session, Room room, string[] parameters)
    {
        var seconds = 60;
        var start = 0;
        if (parameters.Length != 0 && int.TryParse(parameters[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration)) {
            seconds = duration;
            start = 1;
        }
        if (!quizzes.Start(session, string.Join(" ", parameters.Skip(start)), seconds)) {
            session.SendWhisper("Use :wordquiz [1–300 seconds] question in your own room while no question is active.");
        }
    }
}
