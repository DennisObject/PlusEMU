using Plus.Communication.Packets.Outgoing.Rooms.Polls;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Rooms.Polls;

public sealed class RoomWordQuizComponent(TimeProvider clock) : IRoomComponent, IDisposable
{
    private readonly object _sync = new();
    private readonly object _publicationGate = new();
    private bool _publishing;
    private readonly HashSet<int> _voters = new();
    private Room _room = null!;
    private string _question = "";
    private int _questionId;
    private DateTimeOffset _deadline;
    private int _no;
    private int _yes;
    private int _lastVoter;
    private string _lastAnswer = "";
    private bool _disposed;

    public void Initiate(Room room) => _room = room;
    public void Initiated() { }

    public bool Start(GameClient session, int questionId, string question, int seconds)
    {
        if (!EnterPublication()) {
            return false;
        }
        try {
            RoomWordQuizSnapshot? finished;
            RoomWordQuizSnapshot current;
            lock (_sync) {
                if (_disposed || Admitted(session) == null || questionId >= 0) {
                    return false;
                }
                var now = clock.GetUtcNow();
                finished = Finish(now);
                if (_questionId != 0) {
                    return false;
                }
                _questionId = questionId;
                _question = question;
                _deadline = now.AddSeconds(seconds);
                _no = _yes = _lastVoter = 0;
                _lastAnswer = "";
                _voters.Clear();
                current = Snapshot(now);
            }
            PublishFinished(finished);
            if (!_room.MDisposed) {
                _room.SendPacket(new SimplePollStartComposer(current));
            }
            return true;

        }
        finally {
            ExitPublication();
        }
    }

    public void Answer(GameClient session, int questionId, string answer)
    {
        if (!EnterPublication()) {
            return;
        }
        try {
            RoomWordQuizSnapshot? finished;
            RoomWordQuizSnapshot? current = null;
            var userId = 0;
            lock (_sync) {
                if (_disposed || Admitted(session) is not { } habbo) {
                    return;
                }
                var now = clock.GetUtcNow();
                finished = Finish(now);
                userId = habbo.Id;
                if (_questionId == questionId && answer is "0" or "1" && _voters.Add(userId)) {
                    if (answer == "0") {
                        _no++;
                    }
                    else {
                        _yes++;
                    }
                    _lastVoter = userId;
                    _lastAnswer = answer;
                    current = Snapshot(now);
                }
            }
            PublishFinished(finished);
            if (current != null && !_room.MDisposed) {
                _room.SendPacket(new SimplePollAnswerComposer(userId, answer, current.No, current.Yes));
            }

        }
        finally {
            ExitPublication();
        }
    }

    public void Show(GameClient session)
    {
        if (!EnterPublication()) {
            return;
        }
        try {
            RoomWordQuizSnapshot? finished;
            RoomWordQuizSnapshot? current = null;
            var voter = 0;
            var answer = "";
            lock (_sync) {
                if (_disposed || Admitted(session) == null) {
                    return;
                }
                var now = clock.GetUtcNow();
                finished = Finish(now);
                if (_questionId != 0) {
                    current = Snapshot(now);
                    voter = _lastVoter;
                    answer = _lastAnswer;
                }
            }
            PublishFinished(finished);
            if (current != null && !_room.MDisposed) {
                session.Send(new SimplePollStartComposer(current));
                // AIR discards answer events whose avatar has already left; never invent a vote to carry totals.
                if (Admitted(session) != null && voter != 0 &&
                    _room.GetRoomUserManager()?.GetRoomUserByHabbo(voter)?.GetClient() is { } voterSession && Admitted(voterSession) != null) {
                    session.Send(new SimplePollAnswerComposer(voter, answer, current.No, current.Yes));
                }
            }

        }
        finally {
            ExitPublication();
        }
    }

    public void Cycle()
    {
        if (!EnterPublication()) {
            return;
        }
        try {
            RoomWordQuizSnapshot? finished;
            lock (_sync) {
                if (_disposed || _room.MDisposed || _questionId == 0) {
                    return;
                }
                finished = Finish(clock.GetUtcNow());
            }
            PublishFinished(finished);

        }
        finally {
            ExitPublication();
        }
    }

    private RoomWordQuizSnapshot Snapshot(DateTimeOffset now) => new(_questionId, _question,
        (int)Math.Max(0, Math.Ceiling((_deadline - now).TotalMilliseconds)), _no, _yes);

    private RoomWordQuizSnapshot? Finish(DateTimeOffset now)
    {
        if (_questionId == 0 || now < _deadline) {
            return null;
        }
        var snapshot = Snapshot(now);
        _questionId = 0;
        _question = "";
        _voters.Clear();
        return snapshot;
    }

    private Habbo? Admitted(GameClient session)
    {
        var habbo = session.GetHabbo();
        return habbo != null && !habbo.WalletClosed && !_room.MDisposed && ReferenceEquals(habbo.CurrentRoom, _room) &&
            _room.GetRoomUserManager()?.GetRoomUserByHabbo(habbo.Id) is { } actor &&
            ReferenceEquals(actor.GetClient(), session) ? habbo : null;
    }

    private void PublishFinished(RoomWordQuizSnapshot? snapshot)
    {
        if (snapshot != null && !_room.MDisposed) {
            _room.SendPacket(new SimplePollAnswersComposer(snapshot.QuestionId, snapshot.No, snapshot.Yes));
        }
    }

    // Serialize the operation and its outbound packets without holding the state monitor during callbacks.
    // Reentrant callbacks cannot replace the question midway through a broadcast.
    private bool EnterPublication()
    {
        Monitor.Enter(_publicationGate);
        if (_publishing) {
            Monitor.Exit(_publicationGate);
            return false;
        }
        _publishing = true;
        return true;
    }

    private void ExitPublication()
    {
        _publishing = false;
        Monitor.Exit(_publicationGate);
    }

    public void Dispose()
    {
        lock (_sync) {
            _disposed = true;
            _questionId = 0;
            _question = "";
            _voters.Clear();
        }
    }
}
