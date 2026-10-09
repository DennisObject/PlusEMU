using System.Collections.Immutable;
using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredTemporaryFurnitureActions
{
    public static bool Supports(string name) => name is "wf_act_place_furni" or "wf_act_remove_furni";
    public static WiredConfiguration Defaults(string name) => new()
    {
        IntParams = name == "wf_act_place_furni" ? [0, 1, 0, 0, 0, 0] : [0, 100]
    };

    public static WiredConfiguration ForEditor(WiredConfiguration configuration)
    {
        if (configuration.TemporaryPlacement is not { } placement) {
            return configuration;
        }

        return configuration with
        {
            IntParams = [1, placement.TargetIsUser ? 1 : 0, (int)placement.Location, (int)placement.Altitude,
                placement.OffsetX, placement.OffsetY, placement.OffsetAltitudeHundredths,
                configuration.FurniSources.GetValueOrDefault("target", 100), configuration.UserSources.GetValueOrDefault("target", 0),
                placement.SpawnWithVariable ? 1 : 0, placement.ValueIsVariable ? 1 : 0, placement.Value, placement.ValueTarget,
                configuration.FurniSources.GetValueOrDefault("value", 0), configuration.UserSources.GetValueOrDefault("value", 0)],
            Text = string.Join(';', configuration.SecondarySelectedItems) + "\t"
                + configuration.VariableIds.ElementAtOrDefault(0) + "\t" + configuration.VariableIds.ElementAtOrDefault(1)
        };
    }

    // Decode once into the companion policy before save-time template capture. The six legacy fields keep their meaning.
    public static bool TryDecodeEditor(WiredConfiguration proposed, out WiredConfiguration decoded)
    {
        decoded = proposed;
        var p = proposed.IntParams;

        if (p.Length == 6) {
            return true;
        }

        if (p.Length == 7 && p[0] == 0) {
            decoded = proposed with
            {
                IntParams = p.RemoveAt(0),
                TemporaryPlacement = null,
                Snapshots = [],
                SecondarySelectedItems = [],
                FurniSources = ImmutableDictionary<string, int>.Empty,
                UserSources = ImmutableDictionary<string, int>.Empty,
                VariableIds = [],
                Text = ""
            };

            return true;
        }

        if (p.Length != 15 || p[0] != 1 || p[1] is < 0 or > 1 || p[2] is < 0 or > 1 || p[3] is < 0 or > 2
            || p[4] is < -64 or > 64 || p[5] is < -64 or > 64 || p[6] is < -8000 or > 8000
            || !FurniSource(p[7]) || !UserSource(p[8]) || p[9] is < 0 or > 1 || p[10] is < 0 or > 1
            || p[12] is < 0 or > 3 || !FurniSource(p[13]) || !UserSource(p[14])) {
            return false;
        }

        var parts = proposed.Text.Split('\t');

        if (parts.Length != 3 || !WiredMovementConfiguration.TryItemIds(parts[0], out var targets)
            || parts.Skip(1).Any(token => token.Length > 1024 || token.Contains('\n') || token.Contains('\r'))
            || p[9] == 1 && !WiredVariableModule.TryDefinitionId(parts[1], out _)
            || p[9] == 1 && p[10] == 1 && !(WiredVariableModule.TryDefinitionId(parts[2], out _)
                || (parts[2].StartsWith("internal:@", StringComparison.Ordinal) || parts[2].StartsWith("internal:~", StringComparison.Ordinal))
                    && parts[2].Length > 10)) {
            return false;
        }

        decoded = proposed with
        {
            IntParams = [0, 1, 0, 0, 0, 0],
            Text = "",
            SecondarySelectedItems = targets,
            TemporaryPlacement = new(p[1] == 1, (WiredPlaceLocationType)p[2], (WiredPlaceAltitudeType)p[3], p[4], p[5], p[6],
                p[9] == 1, p[10] == 1, p[11], p[12]),
            FurniSources = proposed.FurniSources.SetItem("target", p[7]).SetItem("value", p[13]),
            UserSources = proposed.UserSources.SetItem("target", p[8]).SetItem("value", p[14]),
            VariableIds = [parts[1], parts[2]]
        };

        return true;
    }

    private static bool UserSource(int source) => source is 0 or 10 or 11 or 200 or 201;

    public static bool TryValidate(string name, WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid temporary furniture configuration.";

        if (!WiredLegacyProtocol.IsWithinLimits(proposed)) {
            return false;
        }

        if (name == "wf_act_place_furni" && !TryDecodeEditor(proposed, out proposed)) {
            return false;
        }

        var p = proposed.IntParams;

        if (name == "wf_act_remove_furni") {
            if (p.Length != 2 || p[0] is < 0 or > 1 || !FurniSource(p[1])) {
                return false;
            }

            validated = proposed with { FurniSources = proposed.FurniSources.SetItem("items", p[1]) };
        }
        else {
            // The six active-editor fields retain their definition/quantity/absolute-location meanings.
            if (p.Length != 6 || p[0] < 0 || p[1] is < 1 or > 10 || p[2] is < 0 or > 1
                || p[3] < 0 || p[4] < 0 || p[5] is < 0 or > 7) {
                return false;
            }

            if (proposed.TemporaryPlacement is { } placement) {
                if (!placement.IsWithinLimits() || !FurniSource(proposed.FurniSources.GetValueOrDefault("target", 100))
                    || proposed.UserSources.GetValueOrDefault("target", 0) is not (0 or 10 or 11 or 200 or 201)) {
                    return false;
                }
            }
        }

        validated = name == "wf_act_place_furni" ? proposed : validated;
        error = "";

        return true;
    }
    private static bool FurniSource(int source) => source is 0 or 100 or 200 or 201;

    public static bool Execute(string name, Item box, WiredRuntimeContext context, WiredConfiguration config, IItemDataManager definitions)
    {
        var handler = context.Room.GetRoomItemHandler();

        if (name == "wf_act_remove_furni") {
            var changed = false;

            // Both editor modes remove temporary identities. They never return or destroy a permanent owned item.
            foreach (var item in context.Targets.ResolveFurni(context, config.SelectedItems, config.FurniSources["items"])) {
                changed |= handler.RemoveTemporaryFloorItem(item);
            }

            return changed;
        }

        if (config.TemporaryPlacement is not { } policy) {
            if (!definitions.Items.TryGetValue((uint)config.IntParams[0], out var definition)) {
                return false;
            }

            var x = config.IntParams[2] == 0 ? box.GetX : config.IntParams[3];
            var y = config.IntParams[2] == 0 ? box.GetY : config.IntParams[4];
            var placed = false;

            for (var i = 0; i < config.IntParams[1]; i++) {
                placed |= handler.PlaceTemporaryFloorItem(definition, box.OwnerId, x, y, config.IntParams[5]) != null;
            }

            return placed;
        }

        if (config.Snapshots.IsEmpty) {
            return false;
        }

        var (targetX, targetY, targetZ) = (0, 0, 0d);

        if (policy.Location == WiredPlaceLocationType.CustomLocation || policy.Altitude == WiredPlaceAltitudeType.CustomAltitude) {
            if (policy.TargetIsUser) {
                var user = context.Targets.ResolveUsers(context, [], config.UserSources.GetValueOrDefault("target", 0)).FirstOrDefault();

                if (user == null) {
                    return false;
                }

                (targetX, targetY, targetZ) = (user.X, user.Y, user.Z);
            }
            else {
                var target = context.Targets.ResolveFurni(context, config.SecondarySelectedItems, config.FurniSources.GetValueOrDefault("target", 100)).FirstOrDefault();

                if (target == null) {
                    return false;
                }

                (targetX, targetY, targetZ) = (target.GetX, target.GetY, target.GetZ);
            }
        }

        var dx = policy.OffsetX;
        var dy = policy.OffsetY;

        if (policy.Location == WiredPlaceLocationType.CustomLocation) {
            dx += targetX - config.Snapshots[0].X;
            dy += targetY - config.Snapshots[0].Y;
        }

        long spawnValue = policy.Value;

        if (policy.SpawnWithVariable && policy.ValueIsVariable && context.VariableFrame is { } valueFrame) {
            using var queries = new WiredVariableQueries(context.Room.GetWired().Variables.Module, valueFrame);
            spawnValue = config.VariableIds.Length > 1 ? queries.ReadOperand((WiredVariableTarget)policy.ValueTarget,
                config.VariableIds[1], config.UserSources.GetValueOrDefault("value", 0), config.FurniSources.GetValueOrDefault("value", 0), config) ?? 0 : 0;
        }

        var placedAny = false;

        foreach (var snapshot in config.Snapshots) {
            if (!definitions.Items.TryGetValue(snapshot.DefinitionId, out var definition)) {
                continue;
            }

            var x = snapshot.X + dx;
            var y = snapshot.Y + dy;

            if (!context.Room.GetGameMap().ValidTile(x, y)) {
                continue;
            }

            var height = policy.Altitude switch
            {
                WiredPlaceAltitudeType.SourceAltitude => snapshot.Z,
                WiredPlaceAltitudeType.CustomAltitude => targetZ,
                _ => context.Room.GetGameMap().SqAbsoluteHeight(x, y)
            };
            var item = handler.PlaceTemporaryFloorItem(definition, box.OwnerId, x, y, snapshot.Rotation,
                Math.Clamp(height + policy.OffsetAltitudeHundredths / 100d, 0, 80), snapshot.State);

            if (item == null) {
                continue;
            }

            placedAny = true;

            // A newly attached item is explicitly added to the child variable frame, never the firing's captured target sets.
            if (policy.SpawnWithVariable && config.VariableIds.Length != 0 && context.VariableFrame is { } parent) {
                var holder = WiredVariableRuntimeFrames.FurniHolder(item);
                var frame = new WiredVariableFrame(parent.RoomId, parent.Holders.Append(holder).ToArray())
                {
                    Context = parent.Context,
                    Trigger = parent.Trigger,
                    Signal = parent.Signal,
                    ResolveSource = parent.ResolveSource,
                    Depth = parent.Depth,
                    ChatText = parent.ChatText
                };
                frame.Selector.AddRange(parent.Selector);
                context.Room.GetWired().Variables.Module.Mutate(new(WiredVariableTarget.Furni, config.VariableIds[0]), holder, WiredVariableMutation.Give, spawnValue, frame);
            }
        }

        return placedAny;
    }
}
