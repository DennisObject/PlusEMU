namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>StableId is a player/item database id; EntityId is the client room unit/item id. Bots/pets are active-only.</summary>
public readonly record struct WiredVariableHolder(WiredVariableTarget Target, long StableId, int EntityId, bool CanPersist = true)
{
    public bool IsTemporaryFurni => Target == WiredVariableTarget.Furni && !CanPersist && StableId == 0 && EntityId < 0;
    // Temporary items have no database identity; each room entity still owns independent active values.
    public long StorageId => IsTemporaryFurni ? EntityId : StableId;
}

/// <summary>One firing. Delayed effects retain this frame; child signals must explicitly choose whether to share its Context.</summary>
public sealed class WiredVariableFrame(uint roomId, IReadOnlyList<WiredVariableHolder> holders)
{
    public uint RoomId { get; } = roomId;
    public Plus.HabboHotel.Items.Wired.Runtime.WiredRuntimeContext? RuntimeContext { get; init; }
    public IReadOnlyList<WiredVariableHolder> Holders { get; } = holders;
    public MemoryWiredVariableStore Context { get; init; } = new();
    public IReadOnlyList<WiredVariableHolder> Trigger { get; init; } = [];
    public IReadOnlyList<WiredVariableHolder> Signal { get; init; } = [];
    public List<WiredVariableHolder> Selector { get; } = [];
    public Func<WiredVariableTarget, int, IEnumerable<uint>, IEnumerable<WiredVariableHolder>>? ResolveSource { get; init; }
    public int Depth { get; init; }
    public string? ChatText { get; init; }
    public bool Contains(WiredVariableHolder holder) => holder.Target is WiredVariableTarget.Global or WiredVariableTarget.Context
        ? holder.StableId == 0 && holder.EntityId == 0 : Holders.Contains(holder);
}

public interface IWiredBuiltinVariables
{
    WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame);
    bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame);
}
