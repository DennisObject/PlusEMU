namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class MovementExecutor(Room room, MovementContext context, RebindService rebind,
    CommitService commit, CommandIntake intake, MovementSearch search, AnnounceService announce,
    ActorTickService ticks) : IMovementEngine
{
    private readonly HashSet<RoomUser> _committed = new(ReferenceEqualityComparer.Instance);
    public void Tick()
    {
        _committed.Clear();
        var actors = room.GetRoomUserManager().GetUserList().OrderBy(a => a.VirtualId).ToArray();

        foreach (var actor in actors)
        {
            PhaseA(actor);
        }

        if (room.MDisposed)
        {
            return;
        }

        search.Run();

        foreach (var actor in actors)
        {
            PhaseC(actor);
        }

        context.Claims.ReleaseRollers();

        if (!room.MDisposed)
        {
            ticks.EndCycle();
        }
    }
    private void PhaseA(RoomUser actor)
    {
        var state = actor.Movement;

        if (state.State != NavState.Active)
        {
            return;
        }

        state.TickLocationRevision = state.LocationRevision;
        rebind.Rebind(actor);
        context.RefreshMembership(actor);

        if (!ticks.BeforeMovement(actor))
        {
            return;
        }

        if (commit.Commit(actor))
        {
            _committed.Add(actor);
        }

        if (Eligible(actor))
        {
            intake.Consume(actor);
        }
    }
    private void PhaseC(RoomUser actor)
    {
        if (!Eligible(actor))
        {
            return;
        }

        announce.Announce(actor, _committed.Contains(actor));

        if (!Eligible(actor))
        {
            return;
        }

        announce.SynchronizeHorse(actor);

        if (Eligible(actor))
        {
            ticks.AfterMovement(actor);
        }
    }
    private static bool Eligible(RoomUser actor) => actor.Movement.State == NavState.Active
        && actor.Movement.TickLocationRevision == actor.Movement.LocationRevision;
}
