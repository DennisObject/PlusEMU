using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;

namespace Plus.HabboHotel.Rooms.Polls;

public interface IRoomWordQuizService
{
    bool Start(GameClient session, string question, int seconds);
    void Answer(GameClient session, int pollId, int questionId, string[] answers);
    void Show(GameClient session);
}

public sealed class RoomWordQuizService : IRoomWordQuizService
{
    private int _nextQuestionId;

    public bool Start(GameClient session, string question, int seconds)
    {
        var habbo = session.GetHabbo();
        var room = habbo?.CurrentRoom;
        if (room == null || habbo == null || !habbo.Access.Can(PermissionKeys.CommandWordquiz) ||
            room.OwnerId != habbo.Id && !habbo.Access.Can(PermissionKeys.RoomOwnerAny) ||
            seconds is < 1 or > 300 || string.IsNullOrWhiteSpace(question) || question.Length > 500) {
            return false;
        }
        var id = Interlocked.Decrement(ref _nextQuestionId);
        return id < 0 && Component(room)?.Start(session, id, question.Trim(), seconds) == true;
    }

    public void Answer(GameClient session, int pollId, int questionId, string[] answers)
    {
        if (pollId == 0 && questionId < 0 && answers.Length == 1 && answers[0] is "0" or "1" &&
            session.GetHabbo()?.CurrentRoom is { } room) {
            Component(room)?.Answer(session, questionId, answers[0]);
        }
    }

    public void Show(GameClient session)
    {
        if (session.GetHabbo()?.CurrentRoom is { } room) {
            Component(room)?.Show(session);
        }
    }

    private static RoomWordQuizComponent? Component(Room room) => room.Components.OfType<RoomWordQuizComponent>().SingleOrDefault();
}
