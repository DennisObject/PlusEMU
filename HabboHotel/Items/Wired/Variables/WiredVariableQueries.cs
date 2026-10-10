using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>One selector/addon evaluation. Capture reference operands once; dispose before the next box executes.</summary>
public sealed class WiredVariableQueries(WiredVariableModule variables, WiredVariableFrame frame) : IDisposable
{
    private readonly Dictionary<WiredVariableReference, WiredVariableReadSnapshot> _reads = [];
    private readonly Dictionary<(string Name, WiredConfiguration Configuration), long?> _operands = [];
    private bool _disposed;

    public bool MatchSelector(string name, WiredConfiguration configuration, WiredVariableHolder holder)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var target = name switch
        {
            "wf_slc_furni_with_var" => WiredVariableTarget.Furni,
            "wf_slc_users_with_var" => WiredVariableTarget.User,
            _ => (WiredVariableTarget)(-1)
        };
        var p = configuration.IntParams;
        var ids = configuration.VariableIds;

        if (holder.Target != target || p.Length != 9 || p[0] is not (0 or 1) || p[1] is < 0 or > 5
            || p[2] is not (0 or 1) || !Enum.IsDefined((WiredVariableTarget)p[4]) || ids.Length != 2
            || Resolve(target, ids[0]) is not { } variable) {
            return false;
        }

        var value = Read(variable, holder);

        if (value is null) {
            return false;
        }

        if (p[0] == 0) {
            return true;
        }

        long operand = p[3];

        if (p[2] == 1) {
            if (Resolve((WiredVariableTarget)p[4], ids[1]) is not { } reference) {
                return false;
            }

            if (!_operands.TryGetValue((name, configuration), out var captured)) {

                foreach (var source in WiredVariableExecutors.Select(frame, reference.Target, p[5], p[6], WiredVariableExecutors.Picks(configuration, p[6]))) {
                    if (Read(reference, source) is { } found) {
                        captured = found.Value;
                        break;
                    }
                }

                _operands[(name, configuration)] = captured;
            }

            if (captured is not { } capturedOperand) {
                return false;
            }

            operand = capturedOperand;
        }

        return WiredVariablePredicates.Compare(p[1], value.Value, operand);
    }

    /// <summary>A picked variable is an opaque catalog id; it resolves through the room module's current authority.</summary>
    private WiredVariableReference? Resolve(WiredVariableTarget target, string variableId) =>
        Enum.IsDefined(target) && !WiredVariableAbsent.Is(variableId) && variables.TryResolveCatalogId(variableId, target, out var reference)
            ? reference : null;

    public long? ReadOperand(WiredVariableTarget target, string variableId, int userSource, int furniSource, WiredConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Resolve(target, variableId) is not { } reference) {
            return null;
        }

        foreach (var holder in WiredVariableExecutors.Select(frame, target, userSource, furniSource, WiredVariableExecutors.Picks(configuration, furniSource))) {
            if (Read(reference, holder) is { } value) {
                return value.Value;
            }
        }

        return null;
    }

    private WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder)
    {
        if (!_reads.TryGetValue(reference, out var snapshot)) {
            _reads[reference] = snapshot = variables.CaptureReads([reference], frame);
        }

        return snapshot.Read(reference, holder, frame);
    }
    public void Dispose()
    {
        _disposed = true;

        foreach (var snapshot in _reads.Values) {
            snapshot.Dispose();
        }

        _reads.Clear();
        _operands.Clear();
    }
}
