using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>One selector/addon evaluation. Capture reference operands once; dispose before the next box executes.</summary>
public sealed class WiredVariableQueries(WiredVariableModule variables, WiredVariableFrame frame) : IDisposable
{
    private readonly Dictionary<WiredVariableReference, WiredVariableReadSnapshot> _reads = [];
    private readonly Dictionary<(string Name, WiredConfiguration Configuration), Dictionary<WiredVariableHolder, int>> _operands = [];
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
        var tokens = configuration.Text.Split('\t');

        if (holder.Target != target || p.Length != 9 || p[0] is not (0 or 1) || p[1] is < 0 or > 5
            || p[2] is not (0 or 1) || !Enum.IsDefined((WiredVariableTarget)p[4]) || tokens[0].Length == 0)
        {
            return false;
        }

        var value = Read(new(target, tokens[0]), holder);

        if (value is null)
        {
            return false;
        }

        if (p[0] == 0)
        {
            return true;
        }

        var operand = p[3];

        if (p[2] == 1)
        {
            if (tokens.Length < 2 || tokens[1].Length == 0)
            {
                return false;
            }

            if (!_operands.TryGetValue((name, configuration), out var operands))
            {
                var reference = new WiredVariableReference((WiredVariableTarget)p[4], tokens[1]);
                operands = [];

                foreach (var source in WiredVariableExecutors.Select(frame, reference.Target, p[5], p[6], configuration.SelectedItems))
                {
                    if (Read(reference, source) is { } found)
                    {
                        operands[source] = found.Value;
                    }
                }

                _operands[(name, configuration)] = operands;
            }

            if (operands.Count == 0)
            {
                return false;
            }

            operand = operands.TryGetValue(holder, out var sameHolder) ? sameHolder : operands.First().Value;
        }

        return WiredVariablePredicates.Compare(p[1], value.Value, operand);
    }

    public long? ReadOperand(WiredVariableTarget target, string token, int userSource, int furniSource, WiredConfiguration configuration)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Enum.IsDefined(target) || string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var reference = new WiredVariableReference(target, token);

        foreach (var holder in WiredVariableExecutors.Select(frame, target, userSource, furniSource, configuration.SelectedItems))
        {
            if (Read(reference, holder) is { } value)
            {
                return value.Value;
            }
        }

        return null;
    }

    private WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder)
    {
        if (!_reads.TryGetValue(reference, out var snapshot))
        {
            _reads[reference] = snapshot = variables.CaptureReads([reference], frame);
        }

        return snapshot.Read(reference, holder, frame);
    }
    public void Dispose()
    {
        _disposed = true;

        foreach (var snapshot in _reads.Values)
        {
            snapshot.Dispose();
        }

        _reads.Clear();
        _operands.Clear();
    }
}
