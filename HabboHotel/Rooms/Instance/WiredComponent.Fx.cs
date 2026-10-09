using Plus.Core;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Rooms.Instance;

public partial class WiredComponent
{
    private sealed class ViewerObjects
    {
        public Dictionary<uint, Item> Furni { get; } = [];
        public Dictionary<int, RoomUser> Users { get; } = [];
    }
    private readonly Dictionary<RoomUser, ViewerObjects> _fxViewers = [];

    // A successful Send means packet composition and enqueue, not a network receipt.
    internal void SnapshotEnqueued(GameClient session, IEnumerable<Item> furniture, IEnumerable<RoomUser> users) => _engine.Mutate(() =>
    {
        var viewer = _room.GetRoomUserManager().GetRoomUserByHabbo(session.GetHabbo().Id);

        if (viewer == null || viewer.IsBot || !ReferenceEquals(viewer.GetClient(), session)) {
            return false;
        }

        var ready = new ViewerObjects();

        foreach (var item in furniture) {
            ready.Furni[item.Id] = item;
        }

        foreach (var user in users) {
            ready.Users[user.VirtualId] = user;
        }

        _fxViewers[viewer] = ready;
        session.Send(new Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired.WiredEnvironmentComposer(_clickUserTriggers.Count != 0, _room.Id));

        if (_variables?.IsValueCreated == true) {
            _variables.Value.InvalidateFx();
        }

        return true;
    });

    internal void ObjectEnqueued(RoomUser viewer, Item? item, RoomUser? user) => _engine.Mutate(() =>
    {
        // Incremental objects cannot make a joining viewer ready before the full room snapshot.
        if (!_fxViewers.TryGetValue(viewer, out var ready)) {
            return false;
        }

        if (item != null) {
            ready.Furni[item.Id] = item;
        }

        if (user != null) {
            ready.Users[user.VirtualId] = user;
        }

        if (_variables?.IsValueCreated == true) {
            _variables.Value.InvalidateFx();
        }

        return true;
    });

    private void ForgetFxItem(Item item)
    {
        foreach (var ready in _fxViewers.Values) {
            if (ready.Furni.TryGetValue(item.Id, out var captured) && ReferenceEquals(captured, item)) {
                ready.Furni.Remove(item.Id);
            }
        }
    }

    private void ForgetFxActor(RoomUser user)
    {
        _fxViewers.Remove(user);

        foreach (var ready in _fxViewers.Values) {
            if (ready.Users.TryGetValue(user.VirtualId, out var captured) && ReferenceEquals(captured, user)) {
                ready.Users.Remove(user.VirtualId);
            }
        }
    }

    internal IReadOnlyList<WiredVariableFxViewer> CaptureFxViewers(WiredRuntimeContext? context = null)
    {
        var users = _targets.AllUsers().ToHashSet();
        var viewers = new List<WiredVariableFxViewer>();

        foreach (var (viewer, ready) in _fxViewers.ToArray()) {
            if (!users.Contains(viewer)) {
                _fxViewers.Remove(viewer);
                continue;
            }

            foreach (var item in ready.Furni.Values.ToArray()) {
                if (!_targets.IsAttached(item)) {
                    ready.Furni.Remove(item.Id);
                }
            }

            foreach (var user in ready.Users.Values.ToArray()) {
                if (!users.Contains(user)) {
                    ready.Users.Remove(user.VirtualId);
                }
            }

            // Capture only successfully enqueued, still-attached identities; selectors remain floor-only.
            if (context != null) {
                foreach (var item in ready.Furni.Values) {
                    context.FurniIdentity[item.Id] = item;
                }
            }

            viewers.Add(new(viewer, ready.Furni.Values.Select(WiredVariableRuntimeFrames.FurniHolder)
                .Concat(ready.Users.Values.Select(WiredVariableRuntimeFrames.UserHolder)).ToArray()));
        }

        return viewers;
    }

    private void FlushVariableFx() => _engine.Mutate(() =>
    {
        if (_variables?.IsValueCreated != true || _fxViewers.Count == 0) {
            return false;
        }

        try {
            var context = new WiredRuntimeContext(_room, new(WiredEventKind.Periodic), _targets, this)
            { NowMilliseconds = _engine.NowMilliseconds };
            var frame = WiredVariableRuntimeFrames.Create(context);
            var viewers = CaptureFxViewers(context);
            // Walls have their own client snapshot; FX may read them without expanding floor selectors.
            var holders = frame.Holders.Concat(viewers.SelectMany(viewer => viewer.ReadyHolders)).Distinct().ToArray();
            var fxFrame = new WiredVariableFrame(_room.Id, holders) { RuntimeContext = context };

            return _variables.Value.FlushFx(fxFrame, viewers, (client, packet) => client.Send(packet), ExceptionLogger.LogException);
        }
        catch (Exception exception) {
            _variables.Value.InvalidateFx();
            ExceptionLogger.LogException(exception);

            return false;
        }
    });
}
