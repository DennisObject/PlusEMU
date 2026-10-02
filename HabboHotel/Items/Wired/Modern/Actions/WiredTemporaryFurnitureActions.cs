using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredTemporaryFurnitureActions
{
    public static bool Supports(string name) => name is "wf_act_place_furni" or "wf_act_remove_furni";
    public static WiredConfiguration Defaults(string name) => new() {
        IntParams = name == "wf_act_place_furni" ? [0, 1, 0, 0, 0, 0] : [0, 100]
    };

    public static bool TryValidate(string name, WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed; error = "Invalid temporary furniture configuration.";
        if (!WiredLegacyProtocol.IsWithinLimits(proposed)) return false;
        var p = proposed.IntParams;
        if (name == "wf_act_remove_furni")
        {
            if (p.Length != 2 || p[0] is < 0 or > 1 || !FurniSource(p[1])) return false;
            validated = proposed with { FurniSources = proposed.FurniSources.SetItem("items", p[1]) };
        }
        else
        {
            // The six active-editor fields retain their definition/quantity/absolute-location meanings.
            if (p.Length != 6 || p[0] < 0 || p[1] is < 1 or > 10 || p[2] is < 0 or > 1
                || p[3] is < 0 or > 63 || p[4] is < 0 or > 63 || p[5] is < 0 or > 7) return false;
            if (proposed.TemporaryPlacement is { } placement)
            {
                if (!placement.IsWithinLimits() || !FurniSource(proposed.FurniSources.GetValueOrDefault("target", 100))
                    || proposed.UserSources.GetValueOrDefault("target", 0) is not (0 or 10 or 11 or 200 or 201)) return false;
            }
        }
        error = ""; return true;
    }
    private static bool FurniSource(int source) => source is 0 or 100 or 200 or 201;

    public static bool Execute(string name, Item box, WiredRuntimeContext context, WiredConfiguration config)
    {
        var handler = context.Room.GetRoomItemHandler();
        if (name == "wf_act_remove_furni")
        {
            var changed = false;
            // Both editor modes remove temporary identities. They never return or destroy a permanent owned item.
            foreach (var item in context.Targets.ResolveFurni(context, config.SelectedItems, config.FurniSources["items"]))
                changed |= handler.RemoveTemporaryFloorItem(item);
            return changed;
        }
        if (config.TemporaryPlacement is not { } policy)
        {
            if (!PlusEnvironment.Game.ItemManager.Items.TryGetValue((uint)config.IntParams[0], out var definition)) return false;
            var x = config.IntParams[2] == 0 ? box.GetX : config.IntParams[3];
            var y = config.IntParams[2] == 0 ? box.GetY : config.IntParams[4];
            var placed = false;
            for (var i = 0; i < config.IntParams[1]; i++)
                placed |= handler.PlaceTemporaryFloorItem(definition, box.OwnerId, x, y, config.IntParams[5]) != null;
            return placed;
        }
        if (config.Snapshots.IsEmpty) return false;
        var (targetX, targetY, targetZ) = (0, 0, 0d);
        if (policy.Location == WiredPlaceLocationType.CustomLocation || policy.Altitude == WiredPlaceAltitudeType.CustomAltitude)
        {
            if (policy.TargetIsUser)
            {
                var user = context.Targets.ResolveUsers(context, [], config.UserSources.GetValueOrDefault("target", 0)).FirstOrDefault();
                if (user == null) return false;
                (targetX, targetY, targetZ) = (user.X, user.Y, user.Z);
            }
            else
            {
                var target = context.Targets.ResolveFurni(context, config.SecondarySelectedItems, config.FurniSources.GetValueOrDefault("target", 100)).FirstOrDefault();
                if (target == null) return false;
                (targetX, targetY, targetZ) = (target.GetX, target.GetY, target.GetZ);
            }
        }
        var dx = policy.OffsetX; var dy = policy.OffsetY;
        if (policy.Location == WiredPlaceLocationType.CustomLocation) { dx += targetX - config.Snapshots[0].X; dy += targetY - config.Snapshots[0].Y; }
        var spawnValue = policy.Value;
        if (policy.SpawnWithVariable && policy.ValueIsVariable && context.VariableFrame is { } valueFrame)
        {
            using var queries = new WiredVariableQueries(context.Room.GetWired().Variables.Module, valueFrame);
            spawnValue = config.VariableIds.Length > 1 ? (int)Math.Clamp(queries.ReadOperand((WiredVariableTarget)policy.ValueTarget,
                config.VariableIds[1], config.UserSources.GetValueOrDefault("value", 0), config.FurniSources.GetValueOrDefault("value", 0), config) ?? 0, int.MinValue, int.MaxValue) : 0;
        }
        var placedAny = false;
        foreach (var snapshot in config.Snapshots)
        {
            if (!PlusEnvironment.Game.ItemManager.Items.TryGetValue(snapshot.DefinitionId, out var definition)) continue;
            var x = snapshot.X + dx; var y = snapshot.Y + dy;
            if (!context.Room.GetGameMap().ValidTile(x, y)) continue;
            var height = policy.Altitude switch {
                WiredPlaceAltitudeType.SourceAltitude => snapshot.Z,
                WiredPlaceAltitudeType.CustomAltitude => targetZ,
                _ => context.Room.GetGameMap().SqAbsoluteHeight(x, y)
            };
            var item = handler.PlaceTemporaryFloorItem(definition, box.OwnerId, x, y, snapshot.Rotation,
                Math.Clamp(height + policy.OffsetAltitudeHundredths / 100d, 0, 80), snapshot.State);
            if (item == null) continue;
            placedAny = true;
            // A newly attached item is explicitly added to the child variable frame, never the firing's captured target sets.
            if (policy.SpawnWithVariable && config.VariableIds.Length != 0 && context.VariableFrame is { } parent)
            {
                var holder = new WiredVariableHolder(WiredVariableTarget.Furni, 0, unchecked((int)item.Id), false);
                var frame = new WiredVariableFrame(parent.RoomId, parent.Holders.Append(holder).ToArray()) {
                    Context = parent.Context, Trigger = parent.Trigger, Signal = parent.Signal,
                    ResolveSource = parent.ResolveSource, Depth = parent.Depth, ChatText = parent.ChatText
                };
                frame.Selector.AddRange(parent.Selector);
                context.Room.GetWired().Variables.Module.Mutate(new(WiredVariableTarget.Furni, config.VariableIds[0]), holder, WiredVariableMutation.Give, spawnValue, frame);
            }
        }
        return placedAny;
    }
}
