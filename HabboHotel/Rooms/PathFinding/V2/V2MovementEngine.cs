using Plus.Database;
using Plus.HabboHotel.Quests;

namespace Plus.HabboHotel.Rooms.PathFinding;

internal sealed class V2MovementEngine : IMovementEngine
{
    private readonly MovementExecutor _executor;
    private readonly MovementCancellation _cancellation;
    private readonly ForcePlacementService _placement;
    private readonly AdmissionService _admission;
    private readonly RebindService _rebind;
    internal ClaimLedger Claims => Context.Claims;
    internal MovementContext Context { get; }
    internal V2MovementEngine(Room room, RoomNavigation navigation, IDatabase database)
    {
        Context = new(room, navigation, new LandingEffects(room, database),
            new FloorEffectService(room, client => RewardTrackManager.Current?.Progress(client, RewardTrackActions.Swim)));
        _cancellation = new(Context);
        _placement = new(room, navigation, Context, _cancellation);
        _admission = new(room, Context, _placement);
        _rebind = new RebindService(room, navigation.Grid);
        var commit = new CommitService(room, navigation, Context, _cancellation);
        var intake = new CommandIntake(Context);
        var search = new MovementSearch(room, navigation, Context, _cancellation);
        var announce = new AnnounceService(room, navigation, Context, _cancellation);
        _executor = new(room, Context, _rebind, commit, intake, search, announce, new(room));
    }
    internal void RefreshPostures()
    {
        foreach (var actor in Context.Room.GetRoomUserManager().GetUserList())
        {
            if (actor.Movement.State != NavState.Active) continue;
            _rebind.Rebind(actor); Context.RefreshMembership(actor);
        }
    }
    public void Tick() => _executor.Tick();
    internal void Handle(RoomCommand command)
    {
        var actor = command.Actor;
        if (command.LifetimeId != actor.Movement.LifetimeId) return;
        if (command.Kind == RoomCommandKind.Remove) { _cancellation.Remove(actor); return; }
        if (actor.Movement.State == NavState.Removing) return;
        switch (command.Kind)
        {
            case RoomCommandKind.Admit: _admission.Admit(actor); break;
            case RoomCommandKind.Cancel: _cancellation.Cancel(actor, command.CommandSequence); break;
            case RoomCommandKind.ForcePlace: _placement.Place(actor, command); break;
        }
    }
}
