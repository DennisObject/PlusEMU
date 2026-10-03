namespace Plus.HabboHotel.Rooms.PathFinding;

public enum ClaimKind : byte { Exclusive, Goal, Shared, Roller }

public sealed class ClaimMember(RoomUser actor)
{
    public RoomUser Actor { get; } = actor;
    public long GroupId { get; internal set; }
    public int? Slot { get; internal set; }
    public int Tile { get; internal set; }
    public bool Walking { get; internal set; }
    public ClaimMember? Next { get; internal set; }
    internal List<(int Slot, ClaimKind Kind)> Claims { get; } = new(4);
}

// Only the room owner mutates membership or claims. Group exclusion applies to both.
public sealed class ClaimLedger
{
    private readonly Dictionary<RoomUser, ClaimMember> _members = new(ReferenceEqualityComparer.Instance);
    private readonly List<Claim>?[] _claims;
    private readonly record struct Claim(ClaimMember Owner, ClaimKind Kind);
    private enum ReleaseMode { All, Batch, Roller }
    public ClaimMember?[] Head { get; }
    public ClaimMember?[] OffGraphHead { get; }
    public int[] Count { get; }
    public int[] StationaryCount { get; }
    public int[] TileCount { get; }

    public ClaimLedger(int slotCapacity, int tileCount)
    {
        Head = new ClaimMember?[slotCapacity]; OffGraphHead = new ClaimMember?[tileCount];
        Count = new int[slotCapacity]; StationaryCount = new int[slotCapacity];
        TileCount = new int[tileCount]; _claims = new List<Claim>?[slotCapacity];
    }

    public ClaimMember Move(RoomUser actor, int? slot, int tile, bool walking, long groupId)
    {
        if (_members.TryGetValue(actor, out var member))
        {
            if (member.Slot == slot && member.Tile == tile)
            {
                if (slot is { } current && member.Walking != walking)
                    StationaryCount[current] += walking ? -1 : 1;
                member.Walking = walking; member.GroupId = groupId;
                return member;
            }
            Unlink(member);
        }
        else { member = new(actor); _members.Add(actor, member); }
        member.Slot = slot; member.Tile = tile; member.Walking = walking; member.GroupId = groupId;
        Link(member);
        return member;
    }

    public void Remove(RoomUser actor)
    {
        if (!_members.TryGetValue(actor, out var member)) return;
        ReleaseClaims(member, ReleaseMode.All); Unlink(member); _members.Remove(actor);
    }

    public PlanningOccupancy Snapshot(long excludingGroup)
    {
        var snapshot = new PlanningOccupancy(Head.Length);
        for (var slot = 0; slot < Head.Length; slot++) snapshot.Targets[slot] = OccupancyAt(slot, excludingGroup);
        return snapshot;
    }

    public bool TryClaim(RoomUser actor, int slot, ClaimKind kind, TargetOccupancy blockingMask)
    {
        if (!_members.TryGetValue(actor, out var member)) return false;
        if ((OccupancyAt(slot, member.GroupId) & blockingMask) != 0) return false;
        var claims = _claims[slot] ??= new(4);
        foreach (var existing in claims)
            if (ReferenceEquals(existing.Owner, member) && existing.Kind == kind) return true;
        claims.Add(new(member, kind)); member.Claims.Add((slot, kind));
        return true;
    }

    public void Release(RoomUser actor)
    {
        if (_members.TryGetValue(actor, out var member)) ReleaseClaims(member, ReleaseMode.All);
    }

    public void ReleaseBatch(RoomUser actor)
    {
        if (_members.TryGetValue(actor, out var member)) ReleaseClaims(member, ReleaseMode.Batch);
    }

    public void ReleaseRollers()
    {
        foreach (var member in _members.Values) ReleaseClaims(member, ReleaseMode.Roller);
    }

    public TargetOccupancy OccupancyAt(int slot, long excludingGroup)
    {
        var result = TargetOccupancy.None;
        for (var member = Head[slot]; member != null; member = member.Next)
            if (member.GroupId != excludingGroup)
                result |= member.Walking ? TargetOccupancy.Walking : TargetOccupancy.Stationary;
        if (slot < OffGraphHead.Length)
            for (var member = OffGraphHead[slot]; member != null; member = member.Next)
                if (member.GroupId != excludingGroup) result |= TargetOccupancy.OffGraph;
        if (_claims[slot] is { } claims)
            foreach (var claim in claims)
                if (claim.Owner.GroupId != excludingGroup) result |= ClaimBit(claim.Kind);
        return result;
    }

    private void Link(ClaimMember member)
    {
        if (member.Slot is { } slot)
        {
            member.Next = Head[slot]; Head[slot] = member; Count[slot]++;
            if (!member.Walking) StationaryCount[slot]++;
        }
        else { member.Next = OffGraphHead[member.Tile]; OffGraphHead[member.Tile] = member; }
        TileCount[member.Tile]++;
    }

    private void Unlink(ClaimMember member)
    {
        var head = member.Slot is { } slot ? Head[slot] : OffGraphHead[member.Tile];
        ClaimMember? previous = null;
        for (var current = head; current != null && !ReferenceEquals(current, member); current = current.Next)
            previous = current;
        if (previous != null) previous.Next = member.Next;
        else if (member.Slot is { } firstSlot) Head[firstSlot] = member.Next;
        else OffGraphHead[member.Tile] = member.Next;
        if (member.Slot is { } oldSlot)
        {
            Count[oldSlot]--;
            if (!member.Walking) StationaryCount[oldSlot]--;
        }
        TileCount[member.Tile]--; member.Next = null;
    }

    private void ReleaseClaims(ClaimMember member, ReleaseMode mode)
    {
        for (var index = member.Claims.Count - 1; index >= 0; index--)
        {
            var (slot, kind) = member.Claims[index];
            if (mode == ReleaseMode.Batch && kind == ClaimKind.Roller
                || mode == ReleaseMode.Roller && kind != ClaimKind.Roller) continue;
            var claims = _claims[slot]!;
            for (var entry = claims.Count - 1; entry >= 0; entry--)
                if (ReferenceEquals(claims[entry].Owner, member) && claims[entry].Kind == kind)
                    claims.RemoveAt(entry);
            member.Claims.RemoveAt(index);
        }
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
