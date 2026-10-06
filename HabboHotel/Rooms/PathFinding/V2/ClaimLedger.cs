namespace Plus.HabboHotel.Rooms.PathFinding;

public enum ClaimKind : byte
{
    Exclusive, Goal, Shared, Roller
}

public sealed class ClaimMember(RoomUser actor)
{
    public RoomUser Actor { get; } = actor;
    public long GroupId
    {
        get; internal set;
    }
    public int? Slot
    {
        get; internal set;
    }
    public int Tile
    {
        get; internal set;
    }
    public bool Walking
    {
        get; internal set;
    }
    public ClaimMember? Next
    {
        get; internal set;
    }
    internal List<(int Slot, ClaimKind Kind)> Claims { get; } = new(4);
}

// Only the room owner mutates membership or claims. Group exclusion applies to both.
public sealed class ClaimLedger
{
    private readonly Dictionary<RoomUser, ClaimMember> _members = new(ReferenceEqualityComparer.Instance);
    private readonly NavGrid? _grid;
    private List<Claim>?[] _claims;
    // Roller cargo destinations: R claims owned by no actor, released with the other roller claims.
    private int[] _cargoReservations;
    private readonly List<int> _cargoTiles = new();
    private readonly record struct Claim(ClaimMember Owner, ClaimKind Kind);
    private enum ReleaseMode
    {
        All, Batch, Roller
    }
    // Lifetime ids are positive, so this group excludes nobody (roller cargo belongs to no actor).
    public const long NoGroup = long.MinValue;
    public ClaimMember?[] Head
    {
        get; private set;
    }
    public ClaimMember?[] OffGraphHead
    {
        get;
    }
    public int[] Count
    {
        get; private set;
    }
    public int[] StationaryCount
    {
        get; private set;
    }
    public int[] TileCount
    {
        get;
    }

    public ClaimLedger(int slotCapacity, int tileCount)
    {
        Head = new ClaimMember?[slotCapacity];
        OffGraphHead = new ClaimMember?[tileCount];
        Count = new int[slotCapacity];
        StationaryCount = new int[slotCapacity];
        TileCount = new int[tileCount];
        _claims = new List<Claim>?[slotCapacity];
        _cargoReservations = new int[tileCount];
    }

    // Layered grids map overflow slots back to their tile for off-graph occupancy.
    internal ClaimLedger(NavGrid grid) : this(grid.SlotCapacity, grid.TileCount) => _grid = grid;

    // Overflow slots only grow; existing slot-indexed state keeps its index (§5.3).
    internal void EnsureCapacity(int slots)
    {
        if (slots <= Head.Length)
        {
            return;
        }

        Head = Grow(Head, slots);
        Count = Grow(Count, slots);
        StationaryCount = Grow(StationaryCount, slots);
        _claims = Grow(_claims, slots);
    }

    internal bool Pinned(int slot) => slot >= 0 && slot < Head.Length && (Count[slot] > 0 || _claims[slot] is { Count: > 0 });

    public ClaimMember Move(RoomUser actor, int? slot, int tile, bool walking, long groupId)
    {
        if (_members.TryGetValue(actor, out var member))
        {
            if (member.Slot == slot && member.Tile == tile)
            {
                if (slot is { } current && member.Walking != walking)
                {
                    StationaryCount[current] += walking ? -1 : 1;
                }

                member.Walking = walking;
                member.GroupId = groupId;

                return member;
            }

            Unlink(member);
        }
        else
        {
            member = new(actor);
            _members.Add(actor, member);
        }

        member.Slot = slot;
        member.Tile = tile;
        member.Walking = walking;
        member.GroupId = groupId;
        Link(member);

        return member;
    }

    public void Remove(RoomUser actor)
    {
        if (!_members.TryGetValue(actor, out var member))
        {
            return;
        }

        ReleaseClaims(member, ReleaseMode.All);
        Unlink(member);
        _members.Remove(actor);
    }

    public PlanningOccupancy Snapshot(long excludingGroup)
    {
        var snapshot = new PlanningOccupancy(Head.Length);

        for (var slot = 0; slot < Head.Length; slot++)
        {
            snapshot.Targets[slot] = OccupancyAt(slot, excludingGroup);
        }

        return snapshot;
    }

    // Roller groups pass their own confirmed departures from the slot; nothing else is ignored.
    public bool TryClaim(RoomUser actor, int slot, ClaimKind kind, TargetOccupancy blockingMask,
        IReadOnlySet<RoomUser>? departing = null)
    {
        if (!_members.TryGetValue(actor, out var member))
        {
            return false;
        }

        if ((OccupancyAt(slot, member.GroupId, departing) & blockingMask) != 0)
        {
            return false;
        }

        var claims = _claims[slot] ??= new(4);

        foreach (var existing in claims)
        {
            if (ReferenceEquals(existing.Owner, member) && existing.Kind == kind)
            {
                return true;
            }
        }

        claims.Add(new(member, kind));
        member.Claims.Add((slot, kind));

        return true;
    }

    // A publish released these slots. Claims follow their surface to its new slot, or are dropped
    // when the surface no longer exists, so a reused slot never inherits another surface's claims.
    internal void RemapSlots(IReadOnlyDictionary<int, SurfaceRef> released, Func<SurfaceRef, int> liveSlot)
    {
        var moved = new List<(Claim Claim, SurfaceRef Surface)>();

        foreach (var (slot, surface) in released)
        {
            if (slot >= _claims.Length || _claims[slot] is not { Count: > 0 } claims)
            {
                continue;
            }

            foreach (var claim in claims)
            {
                claim.Owner.Claims.RemoveAll(owned => owned.Slot == slot);
                moved.Add((claim, surface));
            }

            claims.Clear();
        }

        foreach (var (claim, surface) in moved)
        {
            var slot = liveSlot(surface);

            if (slot < 0)
            {
                continue;
            }

            EnsureCapacity(slot + 1);
            var claims = _claims[slot] ??= new(4);

            if (claims.Contains(claim))
            {
                continue;
            }

            claims.Add(claim);
            claim.Owner.Claims.Add((slot, claim.Kind));
        }
    }

    public void Release(RoomUser actor)
    {
        if (_members.TryGetValue(actor, out var member))
        {
            ReleaseClaims(member, ReleaseMode.All);
        }
    }

    public void ReleaseBatch(RoomUser actor)
    {
        if (_members.TryGetValue(actor, out var member))
        {
            ReleaseClaims(member, ReleaseMode.Batch);
        }
    }

    public void ReleaseRollers()
    {
        foreach (var member in _members.Values)
        {
            ReleaseClaims(member, ReleaseMode.Roller);
        }

        foreach (var tile in _cargoTiles)
        {
            _cargoReservations[tile] = 0;
        }

        _cargoTiles.Clear();
    }

    // Cargo excludes a whole destination tile, so its reservation is keyed by tile and never follows a slot.
    public bool TryReserveCargo(int tile, TargetOccupancy blockingMask, IReadOnlySet<RoomUser>? departing)
    {
        foreach (var slot in TileSlots(tile))
        {
            if ((OccupancyAt(slot, NoGroup, departing) & blockingMask) != 0)
            {
                return false;
            }
        }

        if (_cargoReservations[tile]++ == 0)
        {
            _cargoTiles.Add(tile);
        }

        return true;
    }

    public void ReleaseCargo(int tile)
    {
        if (_cargoReservations[tile] > 0 && --_cargoReservations[tile] == 0)
        {
            _cargoTiles.Remove(tile);
        }
    }

    // The tile's own slot plus every other compiled surface on it (only the own slot with K = 1).
    private IEnumerable<int> TileSlots(int tile)
    {
        yield return tile;

        if (_grid == null)
        {
            yield break;
        }

        for (var ordinal = 0; ordinal < _grid.SurfaceCount(tile); ordinal++)
        {
            if (_grid.SurfaceAt(tile, ordinal) != tile && _grid.SurfaceAt(tile, ordinal) < Head.Length)
            {
                yield return _grid.SurfaceAt(tile, ordinal);
            }
        }
    }

    public void ReleaseRollers(RoomUser actor)
    {
        if (_members.TryGetValue(actor, out var member))
        {
            ReleaseClaims(member, ReleaseMode.Roller);
        }
    }

    public TargetOccupancy OccupancyAt(int slot, long excludingGroup) => OccupancyAt(slot, excludingGroup, null);

    public TargetOccupancy OccupancyAt(int slot, long excludingGroup, IReadOnlySet<RoomUser>? departing)
    {
        var result = TargetOccupancy.None;

        for (var member = Head[slot]; member != null; member = member.Next)
        {
            if (Counts(member, excludingGroup, departing))
            {
                result |= member.Walking ? TargetOccupancy.Walking : TargetOccupancy.Stationary;
            }
        }

        var tile = _grid?.TileOf(slot) ?? slot;

        if (tile < OffGraphHead.Length)
        {
            for (var member = OffGraphHead[tile]; member != null; member = member.Next)
            {
                if (Counts(member, excludingGroup, departing))
                {
                    result |= TargetOccupancy.OffGraph;
                }
            }
        }

        if (_claims[slot] is { } claims)
        {
            foreach (var claim in claims)
            {
                if (Counts(claim.Owner, excludingGroup, departing))
                {
                    result |= ClaimBit(claim.Kind);
                }
            }
        }

        if (tile < _cargoReservations.Length && _cargoReservations[tile] != 0)
        {
            result |= TargetOccupancy.RollerClaim;
        }

        return result;
    }

    private static bool Counts(ClaimMember member, long excludingGroup, IReadOnlySet<RoomUser>? departing)
        => member.GroupId != excludingGroup && (departing == null || !departing.Contains(member.Actor));

    private void Link(ClaimMember member)
    {
        if (member.Slot is { } slot)
        {
            member.Next = Head[slot];
            Head[slot] = member;
            Count[slot]++;

            if (!member.Walking)
            {
                StationaryCount[slot]++;
            }
        }
        else
        {
            member.Next = OffGraphHead[member.Tile];
            OffGraphHead[member.Tile] = member;
        }

        TileCount[member.Tile]++;
    }

    private void Unlink(ClaimMember member)
    {
        var head = member.Slot is { } slot ? Head[slot] : OffGraphHead[member.Tile];
        ClaimMember? previous = null;

        for (var current = head; current != null && !ReferenceEquals(current, member); current = current.Next)
        {
            previous = current;
        }

        if (previous != null)
        {
            previous.Next = member.Next;
        }
        else if (member.Slot is { } firstSlot)
        {
            Head[firstSlot] = member.Next;
        }
        else
        {
            OffGraphHead[member.Tile] = member.Next;
        }

        if (member.Slot is { } oldSlot)
        {
            Count[oldSlot]--;

            if (!member.Walking)
            {
                StationaryCount[oldSlot]--;
            }
        }

        TileCount[member.Tile]--;
        member.Next = null;
    }

    private void ReleaseClaims(ClaimMember member, ReleaseMode mode)
    {
        for (var index = member.Claims.Count - 1; index >= 0; index--)
        {
            var (slot, kind) = member.Claims[index];

            if (mode == ReleaseMode.Batch && kind == ClaimKind.Roller
                || mode == ReleaseMode.Roller && kind != ClaimKind.Roller)
            {
                continue;
            }

            var claims = _claims[slot]!;

            for (var entry = claims.Count - 1; entry >= 0; entry--)
            {
                if (ReferenceEquals(claims[entry].Owner, member) && claims[entry].Kind == kind)
                {
                    claims.RemoveAt(entry);
                }
            }

            member.Claims.RemoveAt(index);
        }
    }

    private static T[] Grow<T>(T[] array, int length)
    {
        Array.Resize(ref array, length);

        return array;
    }

    private static TargetOccupancy ClaimBit(ClaimKind kind) => kind switch
    {
        ClaimKind.Exclusive => TargetOccupancy.ExclusiveClaim,
        ClaimKind.Goal => TargetOccupancy.GoalClaim,
        ClaimKind.Shared => TargetOccupancy.SharedClaim,
        ClaimKind.Roller => TargetOccupancy.RollerClaim,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
