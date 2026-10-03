namespace Plus.HabboHotel.Rooms.PathFinding;

public sealed class Connectivity(NavGrid grid)
{
    private readonly int[] _components = new int[grid.SlotCapacity];
    private readonly int[] _queue = new int[grid.SlotCapacity];
    private int _version = -1;
    public long RetainedBytes => 8L * _components.Length;
    public bool SameComponent(int a, int b)
    {
        EnsureCurrent();
        return _components[a] != 0 && _components[a] == _components[b];
    }

    public void EnsureCurrent()
    {
        if (_version == grid.Version) return;
        Array.Clear(_components);
        var component = 0;
        for (var t = 0; t < grid.SlotCapacity; t++)
        {
            if (_components[t] != 0 || !grid.Active(t)) continue;
            _components[t] = ++component;
            var head = 0; var tail = 0; _queue[tail++] = t;
            while (head < tail)
            {
                var n = _queue[head++]; var x = n % grid.Width; var y = n / grid.Width;
                foreach (var (dx, dy) in PathTieBreak.Neighbours)
                {
                    if (!grid.InBounds(x + dx, y + dy)) continue;
                    var next = grid.Tile(x + dx, y + dy);
                    if (_components[next] != 0 || !grid.Active(next)) continue;
                    _components[next] = component; _queue[tail++] = next;
                }
            }
        }
        _version = grid.Version;
    }
}
