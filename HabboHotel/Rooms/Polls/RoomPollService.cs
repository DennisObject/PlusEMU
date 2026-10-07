using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Polls;
using Plus.HabboHotel.GameClients;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms.Polls;

[Singleton]
public interface IRoomPollService
{
    void Offer(GameClient session);
    void Start(GameClient session, int pollId);
    void Answer(GameClient session, int pollId, int questionId, string[] answers);
    void Reject(GameClient session, int pollId);
}

public sealed class RoomPollService(IRoomPollStore store, TimeProvider clock) : IRoomPollService
{
    private readonly ConditionalWeakTable<GameClient, Survey> _surveys = new();

    public void Offer(GameClient session)
    {
        _surveys.Remove(session);
        var habbo = session.GetHabbo();
        if (habbo?.CurrentRoom is not { } room) {
            return;
        }

        if (room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) == null) {
            return;
        }

        var poll = store.Load(room.RoomId);
        if (poll != null && !store.Completed(poll.Id, habbo.Id)) {
            session.Send(new PollOfferComposer(poll));
        }
    }

    public void Start(GameClient session, int pollId)
    {
        var habbo = session.GetHabbo();
        if (habbo?.CurrentRoom is not { } room) {
            return;
        }

        var actor = room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (actor == null) {
            return;
        }

        var poll = store.Load(room.RoomId);
        if (poll == null || poll.Id != pollId || store.Completed(poll.Id, habbo.Id)) {
            return;
        }

        var survey = _surveys.GetValue(session, _ => new Survey());
        lock (survey) {
            survey.Room = new(room);
            survey.Actor = new(actor);
            survey.Poll = poll;
            session.Send(new PollContentsComposer(poll));
        }
    }

    public void Answer(GameClient session, int pollId, int questionId, string[] answers)
    {
        if (!_surveys.TryGetValue(session, out var survey)) {
            return;
        }

        lock (survey) {
            var habbo = session.GetHabbo();
            if (habbo == null || survey.Poll is not { } poll || poll.Id != pollId ||
                survey.Room == null || !survey.Room.TryGetTarget(out var room) ||
                survey.Actor == null || !survey.Actor.TryGetTarget(out var actor) ||
                !ReferenceEquals(habbo.CurrentRoom, room) ||
                !ReferenceEquals(room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id), actor)) {
                return;
            }

            store.Answer(poll, habbo.Id, questionId, answers, clock.GetUtcNow());
        }
    }

    public void Reject(GameClient session, int pollId)
    {
        if (_surveys.TryGetValue(session, out var survey)) {
            lock (survey) {
                if (survey.Poll?.Id == pollId) {
                    survey.Poll = null;
                    survey.Room = null;
                }
            }
        }
    }

    private sealed class Survey
    {
        public WeakReference<Room>? Room;
        public WeakReference<RoomUser>? Actor;
        public RoomPollSnapshot? Poll;
    }
}
