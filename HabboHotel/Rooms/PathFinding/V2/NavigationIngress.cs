namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed partial class RoomNavigation
{
    private readonly RoomCommandQueue _commands = new();
    private V2MovementEngine? _executor;
    private HorseMountService? _mounts;
    internal HorseMountService Mounts => _mounts ??= new(_room, this, Executor.Context);
    public void Move(RoomUser actor, int x, int y, MoveOrigin origin, MoveFlags flags = MoveFlags.None)
    {
        if (!UsesExecutor || actor.Movement.State == NavState.Removing) return;
        var state = actor.Movement;
        state.Commands.Publish(new(state.NextSequence(), x, y, origin, flags));
    }

    public void InteractionStep(RoomUser actor, int x, int y)
    {
        Move(actor, x, y, MoveOrigin.Interaction);
    }

    public void RunOwner(RoomUser actor, Action<RoomUser, long> action)
        => Post(new(RoomCommandKind.ActorAction, actor, actor.Movement.LifetimeId,
            CommandSequence: actor.Movement.Commands.Read()?.Sequence ?? 0, Action: action));
    public void CancelThrough(RoomUser actor, long sequence)
        => Post(new(RoomCommandKind.Cancel, actor, actor.Movement.LifetimeId, CommandSequence: sequence));
    public void ForcePlaceThrough(RoomUser actor, int x, int y, double z, ForceResolution resolution, long sequence)
        => Post(new(RoomCommandKind.ForcePlace, actor, actor.Movement.LifetimeId, x, y, z, resolution, sequence));
    public void Shutdown()
    {
        if (!UsesExecutor) return;
        lock (_room.NavigationSync)
        {
            var manager = _room.GetRoomUserManager();
            if (manager == null) return;
            using var owner = RoomOwnerScope.Enter(_room);
            foreach (var actor in manager.GetUserList()) Remove(actor);
            DrainCommands();
        }
    }
    public void Admit(RoomUser actor) => Post(new(RoomCommandKind.Admit, actor, actor.Movement.LifetimeId));
    public void Cancel(RoomUser actor) => Post(new(RoomCommandKind.Cancel, actor, actor.Movement.LifetimeId, CommandSequence: actor.Movement.Commands.Read()?.Sequence ?? 0));
    public void ForcePlace(RoomUser actor, int x, int y, double z, ForceResolution resolution)
        => Post(new(RoomCommandKind.ForcePlace, actor, actor.Movement.LifetimeId, x, y, z, resolution, actor.Movement.Commands.Read()?.Sequence ?? 0));
    public void Remove(RoomUser actor)
    {
        if (!UsesExecutor || actor.Movement.State == NavState.Removing) return;
        actor.Movement.State = NavState.Removing;
        Post(new(RoomCommandKind.Remove, actor, actor.Movement.LifetimeId));
    }

    private void Post(RoomCommand command)
    {
        if (!UsesExecutor) return;
        if (RoomOwnerScope.IsOwner(_room)) Executor.Handle(command);
        else _commands.Enqueue(command);
    }
    internal V2MovementEngine Executor => _executor ??= new(_room, this, PlusEnvironment.DatabaseManager, PlusEnvironment.Game);
    public void DrainCommands() { if (UsesExecutor) _commands.Drain(Executor.Handle); }
    public void RefreshPostures()
    {
        if (UsesExecutor && RoomOwnerScope.IsOwner(_room))
        {
            ApplyDirty(); Executor.RefreshPostures();
        }
    }
    public void CycleUsers() => Executor.Tick();
}
