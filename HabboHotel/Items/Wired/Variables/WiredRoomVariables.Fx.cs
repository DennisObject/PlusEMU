using Plus.Communication.Packets.Outgoing.WiredVariables;
using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables.Fx;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed record WiredVariableFxViewer(RoomUser User, IReadOnlyList<WiredVariableHolder> ReadyHolders);

public sealed partial class WiredRoomVariables
{
    private readonly Dictionary<uint, WiredVariableMetadataBox> _metadata = [];
    private (Item Item, int X, int Y, double Z, WiredConfiguration Configuration)[] _fxPlacement = [];
    private (RoomUser User, int Team)[] _readyViewers = [];
    private (int ViewerId, WiredVariableHolder Holder)[] _readyObjects = [];

    private WiredVariableMetadataBox? MetadataOn(uint definitionId, string name)
    {
        if (!_definitions.TryGetValue(definitionId, out var definition) || !IsAttached(definition.Item)) {
            return null;
        }

        return _metadata.Values.Where(x => x.Descriptor.CanonicalName == name && IsAttached(x.Item)
            && x.Item.GetX == definition.Item.GetX && x.Item.GetY == definition.Item.GetY)
            .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).FirstOrDefault();
    }
    private bool IsAttached(Item item) => ReferenceEquals(_room.GetRoomItemHandler().GetItem(item.Id), item);

    public IReadOnlyList<WiredVariableFxBinding> FxBindings()
    {
        var authorized = Module.DescribeDefinitions(_definitions.Keys).ToDictionary(x => x.Definition.ItemId);
        var result = new List<WiredVariableFxBinding>();

        foreach (var box in _metadata.Values.Where(x => WiredVariableMetadataBox.IsFx(x.Descriptor.CanonicalName) && IsAttached(x.Item)).OrderBy(x => x.Item.Id)) {
            var target = box.Configuration.IntParams[0] == 0 ? WiredVariableTarget.User : WiredVariableTarget.Furni;
            var definition = _definitions.Values.Where(x => x.Descriptor.CanonicalName == (target == WiredVariableTarget.User ? "wf_var_user" : "wf_var_furni")
                && authorized.ContainsKey(x.Item.Id) && IsAttached(x.Item) && x.Item.GetX == box.Item.GetX && x.Item.GetY == box.Item.GetY)
                .OrderBy(x => x.Item.GetZ).ThenBy(x => x.Item.Id).FirstOrDefault();
            var reference = new WiredVariableReference(target, definition is null ? "" : $"custom:{definition.Item.Id}");

            if (!WiredVariableFxSettings.TryDecode(box.Descriptor.CanonicalName, checked((int)box.Item.Id), box.Configuration, reference, out var binding, out _)) {
                continue;
            }

            var level = definition is null ? null : MetadataOn(definition.Item.Id, "wf_xtra_var_lvlup_system")?.LevelSystem;
            result.Add(binding! with { Level = level is null ? null : level.Level });
        }

        return result;
    }

    /// <summary>Room-serialized flush. The caller supplies only viewers and objects whose room snapshot was enqueued.</summary>
    public bool FlushFx(WiredVariableFrame frame, IReadOnlyList<WiredVariableFxViewer> readyViewers,
        Action<GameClient, IServerPacket> send, Action<Exception> onFailure)
    {
        if (frame.RoomId != _room.Id) {
            throw new ArgumentException("FX frame belongs to a different room.", nameof(frame));
        }

        var placement = _metadata.Values.Cast<IWiredConfiguredItem>().Concat(_definitions.Values)
            .Where(x => IsAttached(x.Item)).OrderBy(x => x.Item.Id)
            .Select(x => (x.Item, x.Item.GetX, x.Item.GetY, x.Item.GetZ, x.Configuration)).ToArray();
        var viewers = readyViewers.Where(x => !x.User.IsBot && x.User.GetClient() is not null && frame.Contains(WiredVariableRuntimeFrames.UserHolder(x.User)))
            .OrderBy(x => x.User.HabboId).ToArray();
        var identities = viewers.Select(x => (x.User, (int)x.User.Team)).ToArray();
        var readyObjects = viewers.SelectMany(x => x.ReadyHolders.Where(frame.Contains).OrderBy(h => h.Target).ThenBy(h => h.EntityId)
            .Select(h => (x.User.HabboId, h))).ToArray();

        if (!_fxPlacement.SequenceEqual(placement) || !_readyViewers.SequenceEqual(identities) || !_readyObjects.SequenceEqual(readyObjects)) {
            FxDirty = true;
        }

        foreach (var previous in _readyViewers) {
            if (!identities.Any(x => ReferenceEquals(x.User, previous.User))) {
                Fx.RemoveViewer(previous.User.HabboId);
            }
        }

        _fxPlacement = placement;
        _readyViewers = identities;
        _readyObjects = readyObjects;

        if (!FxDirty) {
            return false;
        }

        var bindings = FxBindings();
        using var reads = Fx.CaptureReads(frame, bindings);
        int Team(WiredVariableHolder holder) => holder.Target == WiredVariableTarget.User
            ? (int)(_room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId)?.Team ?? Plus.HabboHotel.Rooms.Games.Teams.Team.None) : 0;
        var failed = false;

        foreach (var viewer in viewers) {
            var holder = WiredVariableRuntimeFrames.UserHolder(viewer.User);

            try {
                var batch = Fx.Update(holder, frame, bindings, viewer.ReadyHolders, Team, reads);

                foreach (var packet in WiredVariableFxComposer.ComposeBatch(batch)) {
                    send(viewer.User.GetClient(), packet);
                }

                if (!Fx.Acknowledge(holder.StableId, batch)) {
                    throw new InvalidOperationException("FX batch changed before enqueue acknowledgement.");
                }
            }
            catch (Exception exception) {
                Fx.RemoveViewer(holder.StableId);
                failed = true;
                onFailure(exception);
            }
        }

        FxDirty = failed;

        return true;
    }
}
