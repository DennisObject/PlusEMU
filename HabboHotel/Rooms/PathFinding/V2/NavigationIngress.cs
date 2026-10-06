using Plus.HabboHotel.Items;

namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed partial class RoomNavigation
{
    private readonly RoomCommandQueue _commands = new();
    private V2MovementEngine? _executor;
    private HorseMountService? _mounts;
    internal HorseMountService Mounts => _mounts ??= new(_room, this, Executor.Context);
    public void Move(RoomUser actor, int x, int y, MoveOrigin origin, MoveFlags flags = MoveFlags.None,
        ApproachDescriptor? approach = null)
    {
        if (!UsesExecutor || actor.Movement.State == NavState.Removing)
        {
            return;
        }

        var state = actor.Movement;
        state.Commands.Publish(new(state.NextSequence(), x, y, origin, flags, approach));
    }

    // Safe off the room task: only the item's published record and state generation are read; the
    // approach surface is bound by the owner at intake.
    internal ApproachDescriptor? DescribeApproach(Item item, int actionKind)
        => Inputs.Read(item.Id) is { Removed: false } record
            ? new(item.Id, record.Version, ApproachDescriptor.Unresolved, actionKind, item.StateGeneration) : null;

    internal void ItemStateChanged(uint itemId) => _executor?.Context.Approaches.CancelItem(itemId);

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
        if (!UsesExecutor)
        {
            return;
        }

        lock (_room.NavigationSync)
        {
            var manager = _room.GetRoomUserManager();

            if (manager == null)
            {
                return;
            }

            using var owner = RoomOwnerScope.Enter(_room);

            foreach (var actor in manager.GetUserList())
            {
                Remove(actor);
            }

            DrainCommands();
            Executor.Context.Approaches.Clear();
        }
    }
    public void Admit(RoomUser actor) => Post(new(RoomCommandKind.Admit, actor, actor.Movement.LifetimeId));
    public void Cancel(RoomUser actor) => Post(new(RoomCommandKind.Cancel, actor, actor.Movement.LifetimeId, CommandSequence: actor.Movement.Commands.Read()?.Sequence ?? 0));
    public void ForcePlace(RoomUser actor, int x, int y, double z, ForceResolution resolution)
        => Post(new(RoomCommandKind.ForcePlace, actor, actor.Movement.LifetimeId, x, y, z, resolution, actor.Movement.Commands.Read()?.Sequence ?? 0));
    public void Remove(RoomUser actor)
    {
        if (!UsesExecutor || actor.Movement.State == NavState.Removing)
        {
            return;
        }

        actor.Movement.State = NavState.Removing;
        Post(new(RoomCommandKind.Remove, actor, actor.Movement.LifetimeId));
    }

    private void Post(RoomCommand command)
    {
        if (!UsesExecutor)
        {
            return;
        }

        if (RoomOwnerScope.IsOwner(_room))
        {
            Executor.Handle(command);
        }
        else
        {
            _commands.Enqueue(command);
        }
    }
    private IGateOccupancy? _gateOccupancy;
    internal IGateOccupancy GateOccupancy => _gateOccupancy ??= new ExecutorGateOccupancy(Grid, Executor.Claims);
    internal V2MovementEngine Executor => _executor ??= new(_room, this, _database, _rewards, _access);
    public void DrainCommands()
    {
        if (UsesExecutor)
        {
            _commands.Drain(Executor.Handle);
        }
    }
    public void RefreshPostures()
    {
        if (UsesExecutor && RoomOwnerScope.IsOwner(_room))
        {
            ApplyDirty();
            Executor.RefreshPostures();
        }
    }
    public void CycleUsers() => Executor.Tick();
}
