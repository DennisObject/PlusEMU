namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class ActorMovementState
{
    private static long _nextLifetime;
    private long _sequence;
    private int _state = (int)NavState.PendingAdmission;
    public long LifetimeId { get; } = Interlocked.Increment(ref _nextLifetime);
    public NavState State { get => (NavState)Volatile.Read(ref _state); internal set => Volatile.Write(ref _state, (int)value); }
    public MoveCommandSlot Commands { get; } = new();
    public long ConsumedSequence { get; internal set; }
    public long GoalRevision { get; internal set; }
    internal long TickLocationRevision { get; set; }
    public long LocationRevision { get; internal set; }
    public SurfaceRef? CurrentRef { get; internal set; }
    public double SupportZ { get; internal set; }
    public Route Route { get; } = new();
    public int Cursor { get; internal set; }
    public SurfaceRef[] Pending { get; } = new SurfaceRef[3];
    public int PendingCount { get; internal set; }
    public bool HasIntent { get; internal set; }
    internal bool RouteInvalidated { get; set; }
    internal bool LandingInProgress { get; set; }
    public int WaitTicks { get; internal set; }
    public int BlockReplans { get; internal set; }
    public int StallTicks { get; internal set; }
    public int BoundVersion { get; internal set; } = -1;
    public MoveOrigin Origin { get; internal set; }
    public MoveFlags Flags { get; internal set; }
    internal AcceptedGoal? AcceptedGoal { get; set; }
    public ActorProfile Profile { get; } = new();
    public long NextSequence() => Interlocked.Increment(ref _sequence);
}
