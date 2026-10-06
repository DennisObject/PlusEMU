namespace Plus.HabboHotel.Rooms.PathFinding;

// Components of the permissive supergraph: any two surfaces on adjacent tiles are joined;
// surfaces on the same tile never are (a vertical gap is not an edge).
public sealed class Connectivity(NavGrid grid)
{
    private int[] _components = new int[grid.SlotCapacity];
    private int[] _queue = new int[grid.SlotCapacity];
    private int _version = -1;
    public long RetainedBytes => 8L * _components.Length;
    public bool SameComponent(int a, int b)
    {
        EnsureCurrent();

        return _components[a] != 0 && _components[a] == _components[b];
    }

    public bool SameComponent(int start, in AcceptedGoal goal)
    {
        for (var index = 0; index < goal.Count; index++) {
            if (SameComponent(start, goal[index])) {
                return true;
            }
        }

        return false;
    }

    public void EnsureCurrent()
    {
        if (_version == grid.Version) {
            return;
        }

        if (_components.Length < grid.SlotCapacity) {
            _components = new int[grid.SlotCapacity];
            _queue = new int[grid.SlotCapacity];
        }

        Array.Clear(_components);
        var component = 0;

        for (var slot = 0; slot < grid.SlotCapacity; slot++) {
            if (_components[slot] != 0 || !grid.Active(slot)) {
                continue;
            }

            _components[slot] = ++component;
            Flood(slot, component);
        }

        _version = grid.Version;
    }

    private void Flood(int first, int component)
    {
        var head = 0;
        var tail = 0;
        _queue[tail++] = first;

        while (head < tail) {
            var tile = grid.TileOf(_queue[head++]);
            var x = tile % grid.Width;
            var y = tile / grid.Width;

            foreach (var (dx, dy) in PathTieBreak.Neighbours) {
                if (!grid.InBounds(x + dx, y + dy)) {
                    continue;
                }

                var neighbour = grid.Tile(x + dx, y + dy);

                for (var ordinal = 0; ordinal < grid.SurfaceCount(neighbour); ordinal++) {
                    var next = grid.SurfaceAt(neighbour, ordinal);

                    if (_components[next] != 0 || !grid.Active(next)) {
                        continue;
                    }

                    _components[next] = component;
                    _queue[tail++] = next;
                }
            }
        }
    }
}
