using System.Diagnostics;
using NLog;

namespace Plus.HabboHotel.Rooms.PathFinding;

// Shadow comparison and optional room-owned movement share one compiled graph.
public sealed partial class RoomNavigation
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    private readonly Room _room;
    private readonly Route _route = new();
    private readonly PlanningOccupancy _occupancy;
    private readonly PathSearch _search;
    public PathfindingSettings Settings { get; }
    public NavInputs Inputs { get; }
    public NavGrid Grid { get; }
    public NavGridCompiler Compiler { get; }
    public bool UsesExecutor => Settings.Engine == PathfindingEngine.V2;
    public bool Enabled => Settings.Engine == PathfindingEngine.Shadow;

    public RoomNavigation(Room room, RoomModel model, PathfindingSettings settings)
    {
        _room = room; Settings = settings;
        if (UsesExecutor) room.EnableV2Movement();
        var width = model.MapSizeX; var height = model.MapSizeY;
        var z = new double[width * height]; var states = new SquareState[z.Length];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            z[y * width + x] = model.SqFloorHeight[x, y]; states[y * width + x] = model.SqState[x, y];
        }
        Grid = new(width, height, z, states, model.DoorY * width + model.DoorX, model.DoorZ);
        // Layered surfaces need the v2 executor; legacy and shadow keep the K=1 graph.
        var layered = settings.LayeringEnabled && settings.Engine == PathfindingEngine.V2;
        Inputs = new(width, height); Compiler = new(Grid, Inputs, settings with { LayeringEnabled = layered });
        Compiler.BeforePublish = tiles =>
        {
            if (UsesExecutor && RoomOwnerScope.IsOwner(_room)) Executor.Context.Geometry.BeforePublish(tiles);
        };
        _occupancy = new(Grid.SlotCapacity); _search = new(Grid, settings);
        Inputs.MarkAllDirty();
        if (settings.LayeringEnabled && !layered)
            Logger.Warn("Room {0}: layering requires pathfinding.engine = v2; using single surfaces (K=1).", room.RoomId);
    }

    public void ApplyDirty()
    {
        if (!Enabled && !UsesExecutor) return;
        try { Compiler.ApplyNow(); }
        catch (Exception error)
        {
            Inputs.MarkAllDirty();
            if (UsesExecutor) throw;
            Logger.Warn(error, "Pathfinding shadow compile failed for room {0}; legacy continues.", _room.RoomId);
        }
    }
    public void SetFloorStatus(int x, int y, byte status)
    {
        if (!Grid.InBounds(x, y)) return;
        var t = Grid.Tile(x, y);
        Volatile.Write(ref Grid.FloorStatusOverrides[t], status);
        Inputs.MarkDirty(t);
    }

    // The legacy map rewrote this cell from furniture, which ends an explicit floor status there.
    public void ReleaseFloorStatus(int x, int y)
    {
        if (!Grid.InBounds(x, y)) return;
        var t = Grid.Tile(x, y);
        if (Interlocked.Exchange(ref Grid.FloorStatusOverrides[t], -1) != -1) Inputs.MarkDirty(t);
    }

    public void ReleaseFloorStatuses()
    {
        for (var y = 0; y < Grid.Height; y++)
            for (var x = 0; x < Grid.Width; x++) ReleaseFloorStatus(x, y);
    }

    public void Compare(RoomUser actor, IReadOnlyList<Vector2D> legacyPath, long legacyTicks)
    {
        if (!Enabled) return;
        try { CompareCore(actor, legacyPath, legacyTicks); }
        catch (Exception error) { Logger.Warn(error, "Pathfinding shadow search failed for room {0} actor {1}; legacy continues.", _room.RoomId, actor.VirtualId); }
    }

    internal static bool Diverges(PathOutcome outcome, int steps, int legacyCount) => outcome switch
    {
        PathOutcome.Found => legacyCount != steps + 1,
        PathOutcome.AlreadyThere => legacyCount != 0,
        _ => legacyCount > 0
    };

    private void CompareCore(RoomUser actor, IReadOnlyList<Vector2D> legacyPath, long legacyTicks)
    {
        // Snapshot legacy inputs at its recalc point. Temporary bots have IgnoreUsers,
        // not staff's wall/void override. Group access is refreshed on each search.
        ApplyDirty();
        Array.Clear(_occupancy.Targets);
        var profile = actor.NavigationProfile ??= new ActorProfile();
        profile.LegacyOverride = actor.AllowOverride && actor.BotData?.IsTemporary != true;
        profile.IgnoreUsers = actor.BotData?.IsTemporary == true;
        profile.IgnoreStepHeight = actor.RidingHorse && Settings.RidersIgnoreHeight;
        profile.Walkthrough = _room.RoomBlockingEnabled;
        profile.DiagonalEnabled = _room.GetGameMap().DiagonalEnabled;
        foreach (var other in _room.GetRoomUserManager().GetUserList())
        {
            if (other == actor || actor.RidingHorse && other.VirtualId == actor.HorseId || !Grid.InBounds(other.X, other.Y)) continue;
            var t = Grid.Tile(other.X, other.Y);
            _occupancy.Targets[t] |= !Grid.Active(t) || other.Z != Grid.WalkZ[t] ? TargetOccupancy.OffGraph
                : other.IsWalking ? TargetOccupancy.Walking : TargetOccupancy.Stationary;
        }
        ActorAccessResolver.Live.Refresh(profile, actor.GetClient()?.GetHabbo()?.Id, Grid.GroupId.Where(groupId => groupId != 0).Distinct());
        using var lease = PathWorkspacePool.Rent(Grid.SlotCapacity, profile.LegacyOverride ? Grid.SlotCapacity : Grid.ActiveNodeCount);
        var started = Stopwatch.GetTimestamp();
        var request = new SearchRequest(profile, new(actor.X, actor.Y, actor.Z), actor.GoalX, actor.GoalY, _occupancy);
        var outcome = _search.Find(request, lease.Workspace, _route);
        var elapsed = Stopwatch.GetTimestamp() - started;
        var divergent = Diverges(outcome, _route.Count, legacyPath.Count);
        if (!divergent && outcome == PathOutcome.Found)
            for (var i = 0; i < _route.Count; i++)
            {
                var t = _route.Steps[i].Tile; var old = legacyPath[legacyPath.Count - 2 - i];
                if (old.X != t % Grid.Width || old.Y != t / Grid.Width) { divergent = true; break; }
            }
        if (Random.Shared.NextDouble() < Settings.ShadowLogSample)
            Logger.Info("Pathfinding shadow room={0} actor={1} goal={2},{3} divergence={4} outcome={5} legacy_steps={6} v2_steps={7} legacy_us={8:F2} v2_us={9:F2} expansions={10}",
                _room.RoomId, actor.VirtualId, request.GoalX, request.GoalY, divergent, outcome,
                Math.Max(0, legacyPath.Count - 1), _route.Count, legacyTicks * 1e6 / Stopwatch.Frequency,
                elapsed * 1e6 / Stopwatch.Frequency, lease.Workspace.Expansions);
    }
}
