using System.Drawing;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

/// <summary>
/// Executes the existing Octane movement editors. Resolved movers and targets are separate;
/// the caller owns source selection, scheduling and packet policy.
/// </summary>
public sealed class WiredMovementActions
{
    public delegate bool MoveFurniture(Item item, int x, int y, int rotation, double? height);
    public delegate bool MoveAvatar(RoomUser avatar, Item target, bool slide, bool fastTeleport, int walkMode);
    /// <summary>Runs each mover's step; the step commits through the given (x, y, rotation) move.</summary>
    public delegate bool MoveTogether(IReadOnlyList<Item> movers, Func<Item, Func<int, int, int, bool>, bool> step);

    public static readonly IReadOnlySet<string> Names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "wf_act_rel_mov", "wf_act_set_altitude", "wf_act_move_rotate", "wf_act_move_furni_as_group",
        "wf_act_furni_to_furni", "wf_act_furni_to_user", "wf_act_move_furni_to", "wf_act_match_to_sshot",
        "wf_act_toggle_state", "wf_act_toggle_to_rnd", "wf_act_teleport_to", "wf_act_user_to_furni"
    };

    public bool Execute(string name, WiredConfiguration configuration, IReadOnlyList<Item> movers,
        IReadOnlyList<Item> targets, IReadOnlyList<RoomUser> users,
        MoveFurniture move, MoveAvatar relocate, Action<Item, string> setState,
        Func<Item, Func<string, string?>, bool>? toggleState = null, MoveTogether? together = null)
    {
        if (!WiredMovementConfiguration.TryValidate(name, configuration, out configuration, out _)) {
            return false;
        }

        var p = configuration.IntParams;
        int Param(int index, int fallback = 0) => index < p.Length ? p[index] : fallback;
        var affected = false;

        switch (name.ToLowerInvariant()) {
            case "wf_act_rel_mov":
                var dx = (Param(0, 1) == 0 ? -1 : 1) * Param(1);
                var dy = (Param(2, 1) == 0 ? -1 : 1) * Param(3);

                if (dx == 0 && dy == 0) {
                    return false;
                }

                foreach (var item in movers) {
                    affected |= move(item, item.GetX + dx, item.GetY + dy, item.Rotation, null);
                }

                break;
            case "wf_act_set_altitude":
                if (!WiredRoomOperations.TryAltitude(configuration.Text, out var altitude)) {
                    return false;
                }

                foreach (var item in movers) {
                    var z = Param(0, 2) switch { 0 => item.GetZ + altitude, 1 => item.GetZ - altitude, _ => altitude };
                    affected |= move(item, item.GetX, item.GetY, item.Rotation, Math.Clamp(z, 0, 80));
                }

                break;
            case "wf_act_move_rotate":
                together ??= (items, step) => items.Aggregate(false,
                    (any, item) => step(item, (x, y, rotation) => move(item, x, y, rotation, null)) | any);
                affected = together(movers, (item, step) =>
                {
                    var direction = MovementDirection(Param(0));
                    var offset = WiredRoomOperations.Offset(direction);
                    var rotation = Param(1) switch
                    {
                        2 => (item.Rotation + 2) % 8,
                        4 => (item.Rotation + 6) % 8,
                        // Random turns a quarter either way, like cw/ccw, so 4-direction furniture keeps a valid rotation.
                        6 => (item.Rotation + (Random.Shared.Next(2) == 0 ? 2 : 6)) % 8,
                        _ => item.Rotation
                    };

                    return step(item.GetX + offset.X, item.GetY + offset.Y, rotation);
                });

                break;
            case "wf_act_move_furni_as_group":
                var groupOffset = WiredRoomOperations.Offset(Param(0));

                // Octane/Polaris direction editor: leading edge first, blocked members skipped.
                foreach (var item in movers.OrderByDescending(item => item.GetX * groupOffset.X + item.GetY * groupOffset.Y)) {
                    affected |= move(item, item.GetX + groupOffset.X, item.GetY + groupOffset.Y, item.Rotation, null);
                }

                break;
            case "wf_act_furni_to_furni":
            case "wf_act_move_furni_to":
                if (targets.Count == 0) {
                    return false;
                }

                var target = targets[0];
                var spacing = name.Equals("wf_act_move_furni_to", StringComparison.OrdinalIgnoreCase)
                    ? WiredRoomOperations.Offset(Param(0)) : Point.Empty;

                foreach (var item in movers) {
                    affected |= move(item, target.GetX + spacing.X * Param(1, 1),
                        target.GetY + spacing.Y * Param(1, 1), item.Rotation, null);
                }

                break;
            case "wf_act_furni_to_user":
                if (users.Count == 0) {
                    return false;
                }

                foreach (var item in movers) {
                    affected |= move(item, users[0].X, users[0].Y, item.Rotation, null);
                }

                break;
            case "wf_act_match_to_sshot":
                foreach (var item in movers) {
                    var snapshot = configuration.Snapshots.FirstOrDefault(entry => entry.ItemId == item.Id);

                    if (snapshot == null) {
                        continue;
                    }

                    if (Param(0) == 1 && toggleState != null) {
                        affected |= toggleState(item, current => string.Equals(current, snapshot.State, StringComparison.Ordinal) ? null : snapshot.State);
                    }
                    else if (Param(0) == 1 && !string.Equals(item.LegacyDataString, snapshot.State, StringComparison.Ordinal)) {
                        setState(item, snapshot.State);
                        affected = true;
                    }

                    affected |= move(item, Param(2) == 1 ? snapshot.X : item.GetX,
                        Param(2) == 1 ? snapshot.Y : item.GetY,
                        Param(1) == 1 ? snapshot.Rotation : item.Rotation, Param(3) == 1 ? snapshot.Z : null);
                }

                break;
            case "wf_act_toggle_state":
            case "wf_act_toggle_to_rnd":
                foreach (var item in movers) {
                    var states = item.Definition.Modes;

                    if (states <= 1) {
                        continue;
                    }

                    if (toggleState != null) {
                        var random = name.Equals("wf_act_toggle_to_rnd", StringComparison.OrdinalIgnoreCase);
                        affected |= toggleState(item, current => NextToggleState(current, states, random, Param(0) == 1));
                        continue;
                    }

                    _ = int.TryParse(item.LegacyDataString, out var oldState);
                    var next = name.Equals("wf_act_toggle_to_rnd", StringComparison.OrdinalIgnoreCase)
                        ? Random.Shared.Next(states)
                        : ((oldState + (Param(0) == 1 ? -1 : 1)) % states + states) % states;

                    if (next == oldState) {
                        continue;
                    }

                    setState(item, next.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    affected = true;
                }

                break;
            case "wf_act_teleport_to":
            case "wf_act_user_to_furni":
                if (targets.Count == 0) {
                    return false;
                }

                foreach (var user in users) {
                    var destination = targets[Random.Shared.Next(targets.Count)];
                    // These are distinct legacy editors: teleport's flag changes its animation
                    // delay; user-to-furni always slides and its third slot controls walk continuity.
                    var slide = name.Equals("wf_act_user_to_furni", StringComparison.OrdinalIgnoreCase);
                    affected |= relocate(user, destination, slide, !slide && Param(0) == 1,
                        slide ? Param(2, 1) : 2);
                }

                break;
            default:
                return false;
        }

        return affected;
    }

    // v2 only. Null when the toggle would not change the state.
    private static string? NextToggleState(string current, int states, bool random, bool reverse)
    {
        _ = int.TryParse(current, out var oldState);
        var next = random ? Random.Shared.Next(states) : ((oldState + (reverse ? -1 : 1)) % states + states) % states;

        return next == oldState ? null : next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int MovementDirection(int movement) => movement switch
    {
        >= 0 and <= 7 => movement,
        // Read-only conversion preserves old editor's random/none choices, absent from the current grid.
        8 => Random.Shared.Next(4) * 2,
        9 => Random.Shared.Next(2) == 0 ? 2 : 6,
        10 => Random.Shared.Next(2) == 0 ? 0 : 4,
        _ => -1
    };
}
