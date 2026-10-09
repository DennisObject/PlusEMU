namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>One firing's arithmetic, ordered by operation and written once per variable holder.</summary>
public sealed class WiredVariableChangeBatch
{
    private readonly List<Change> _changes = [];
    private sealed record Change(WiredVariableModule Module, WiredVariableReference Reference, WiredVariableHolder Holder,
        int Operation, long Operand, WiredVariableFrame Frame);

    public bool IsEmpty => _changes.Count == 0;
    public void Add(WiredVariableModule module, WiredVariableReference reference, WiredVariableHolder holder,
        int operation, long operand, WiredVariableFrame frame) => _changes.Add(new(module, reference, holder, operation, operand, frame));

    public bool Flush()
    {
        var changes = _changes.ToArray();
        _changes.Clear();
        var changed = false;

        foreach (var group in changes.GroupBy(change => (change.Module, Reference: change.Module.WriteTarget(change.Reference), change.Holder))) {
            var first = group.First();
            var ordered = group.OrderBy(change => Rank(change.Operation)).ToArray();
            changed |= first.Module.Change(first.Reference, first.Holder, WiredVariableMutation.Set, current =>
                ordered.Aggregate(current, (value, change) => WiredVariableArithmetic.Apply(change.Operation, value, change.Operand)), first.Frame, notifyUnchanged: true);
        }

        return changed;
    }

    public void Clear() => _changes.Clear();

    private static int Rank(int operation) => operation switch
    {
        0 => 0,
        5 => 1,
        3 => 2,
        4 => 3,
        6 => 4,
        1 => 5,
        2 => 6,
        _ => 7
    };
}
