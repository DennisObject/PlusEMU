using System.Collections.Concurrent;

namespace Plus.HabboHotel.Rooms.PathFinding;

public enum MoveOrigin : byte { User, Bot, Wired, StaffCommand, Interaction }

[Flags]
public enum MoveFlags : byte { None = 0, IgnoreUsers = 1, Teleport = 2 }

// Payload only; completion and automatic approach behavior belong to the next stack layer.
public sealed record ApproachDescriptor(uint ItemId, long ItemRecordVersion, SurfaceRef ApproachSurfaceRef, int ActionKind);

public sealed record MoveCommand(long Sequence, int X, int Y, MoveOrigin Origin,
    MoveFlags Flags = MoveFlags.None, ApproachDescriptor? Approach = null);

public sealed class MoveCommandSlot
{
    private MoveCommand? _latest;

    public MoveCommand? Read() => Volatile.Read(ref _latest);

    public bool Publish(MoveCommand command)
    {
        while (true)
        {
            var previous = Read();
            if (previous != null && previous.Sequence >= command.Sequence) return false;
            if (ReferenceEquals(Interlocked.CompareExchange(ref _latest, command, previous), previous))
                return true;
        }
    }
}

public enum NavState : byte { PendingAdmission, Active, Removing }
public enum RoomCommandKind : byte { Admit, Remove, Cancel, ForcePlace, ActorAction }
public enum ForceResolution : byte { ExactZ, NearestAtOrBelow, Highest }

public sealed record RoomCommand(RoomCommandKind Kind, RoomUser Actor, long LifetimeId,
    int X = 0, int Y = 0, double Z = 0, ForceResolution Resolution = ForceResolution.ExactZ, long CommandSequence = 0, Action<RoomUser, long>? Action = null);

public sealed class RoomCommandQueue
{
    private readonly ConcurrentQueue<RoomCommand> _commands = new();

    public void Enqueue(RoomCommand command) => _commands.Enqueue(command);

    // Lifetime validation belongs to the owner handler; ended lifetimes still need Remove.
    public void Drain(Action<RoomCommand> handle)
    {
        while (_commands.TryDequeue(out var command)) handle(command);
    }
}
