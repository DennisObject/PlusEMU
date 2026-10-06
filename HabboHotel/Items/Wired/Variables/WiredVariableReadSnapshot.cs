namespace Plus.HabboHotel.Items.Wired.Variables;

/// <summary>A single synchronous flush's captured reads. Disposal prevents accidental reuse in a later flush.</summary>
public sealed class WiredVariableReadSnapshot : IDisposable
{
    private readonly uint _roomId;
    private readonly Dictionary<(WiredVariableReference, WiredVariableHolder), WiredVariableValue> _values;
    private bool _disposed;
    internal WiredVariableReadSnapshot(uint roomId, Dictionary<(WiredVariableReference, WiredVariableHolder), WiredVariableValue> values)
    {
        _roomId = roomId;
        _values = values;
    }
    public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return frame.RoomId == _roomId && reference.Target == holder.Target && frame.Contains(holder)
            ? _values.GetValueOrDefault((reference, holder)) : null;
    }
    public void Dispose()
    {
        _disposed = true;
        _values.Clear();
    }
}
