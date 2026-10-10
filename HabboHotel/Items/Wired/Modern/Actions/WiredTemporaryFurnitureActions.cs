using System.Collections.Immutable;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public static class WiredTemporaryFurnitureActions
{
    public static bool Supports(string name) => name is "wf_act_place_furni" or "wf_act_remove_furni";
    public static WiredConfiguration Defaults(string name) => name == "wf_act_place_furni"
        ? new() { TemporaryPlacement = new() }
        : new() { IntParams = [0, 100] };

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
            return false;
        }

        var templateSource = config.FurniSources.GetValueOrDefault("templates", WiredSources.Selected);
        var templates = templateSource is WiredSources.Trigger or WiredSources.Selector or WiredSources.Signal
            ? DynamicTemplates(context, templateSource)
            : config.Snapshots;

        if (templates.Count == 0) {
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
            dx += targetX - templates[0].X;
            dy += targetY - templates[0].Y;
        }

        long spawnValue = policy.Value;
        var valueTarget = (WiredVariableTarget)policy.ValueTarget;
        var module = policy.SpawnWithVariable ? context.Room.GetWired().Variables.Module : null;

        if (module != null && policy.ValueIsVariable && context.VariableFrame is { } valueFrame
            && config.VariableIds.Length > 1 && Enum.IsDefined(valueTarget)
            && module.TryResolveCatalogId(config.VariableIds[1], valueTarget, out _)) {
            using var queries = new WiredVariableQueries(module, valueFrame);
            spawnValue = queries.ReadOperand(valueTarget, config.VariableIds[1],
                config.UserSources.GetValueOrDefault("value", 0), config.FurniSources.GetValueOrDefault("value", 0), config) ?? 0;
        }

        var placedAny = false;

        foreach (var snapshot in templates) {
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
            if (module != null && config.VariableIds.Length != 0 && context.VariableFrame is { } parent
                && module.TryResolveCatalogId(config.VariableIds[0], WiredVariableTarget.Furni, out var spawnVariable)) {
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
                module.Mutate(spawnVariable, holder, WiredVariableMutation.Give, spawnValue, frame);
            }
        }

        return placedAny;
    }

    // Sources 0, 200 and 201 are read when the effect fires. Source 100 keeps the saved snapshots.
    private static IReadOnlyList<WiredFurniSnapshot> DynamicTemplates(WiredRuntimeContext context, int source) =>
        context.Targets.ResolveFurni(context, [], source)
            .Where(item => item.IsFloorItem && !item.IsTemporary)
            .Select(WiredRoomOperations.Capture)
            .ToArray();
}
