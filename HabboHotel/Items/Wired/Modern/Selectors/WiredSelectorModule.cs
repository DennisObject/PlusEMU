using System.Globalization;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

/// <summary>Selection behavior uses Octane's per-box fields; inversion/filtering is composed exactly once.</summary>
public static class WiredSelectorModule
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "wf_slc_furni_bytype", "wf_slc_furni_picks", "wf_slc_users_bytype", "wf_slc_users_team",
        "wf_slc_furni_onfurni", "wf_slc_furni_signal", "wf_slc_furni_neighborhood", "wf_slc_furni_area",
        "wf_slc_users_onfurni", "wf_slc_users_byaction", "wf_slc_users_signal", "wf_slc_users_byname",
        "wf_slc_users_neighborhood", "wf_slc_users_area", "wf_slc_users_handitem", "wf_slc_users_group",
        "wf_slc_furni_altitude", "wf_slc_furni_with_var", "wf_slc_users_with_var", "wf_slc_remote"
    ];

    public static WiredSelectorResult SelectRaw(string name, WiredConfiguration configuration,
        WiredSelectorWorld world, WiredSelectorInputs input, Random? remoteRandom = null) =>
        SelectRaw(name, configuration, world, input, [], remoteRandom ?? Random.Shared);

    private static WiredSelectorResult SelectRaw(string name, WiredConfiguration c,
        WiredSelectorWorld world, WiredSelectorInputs input, HashSet<uint> visiting, Random remoteRandom)
    {
        if (!Names.Contains(name, StringComparer.Ordinal)) {
            throw new ArgumentException("Not a supported selector", nameof(name));
        }

        c = WiredSelectorConfiguration.Normalize(name, c);

        var available = world.Furni.Where(x => x.IsFloor && (input.IncludeWired || !x.IsWired)).ToList();
        var selected = new WiredSelectedIds();
        var target = name == "wf_slc_remote" ? WiredSelectorTarget.Both
            : name.StartsWith("wf_slc_users_", StringComparison.Ordinal) ? WiredSelectorTarget.User : WiredSelectorTarget.Furni;
        var (filterIndex, invertIndex) = SwitchIndexes(name);
        var filter = WiredSelectorSources.Param(c, filterIndex) == 1;
        var invert = WiredSelectorSources.Param(c, invertIndex) == 1;
        int P(int index, int fallback = 0) => WiredSelectorSources.Param(c, index, fallback);
        IEnumerable<WiredSelectorFurniture> Furni(int source) =>
            world.Furni.Where(x => WiredSelectorSources.Furni(source, c, input, world).Contains(x.Id));

        switch (name) {
            case "wf_slc_furni_picks":
                selected.FurniIds.UnionWith(world.Furni.Where(x => (input.IncludeWired || !x.IsWired) && c.SelectedItems.Contains(x.Id)).Select(x => x.Id));
                break;
            case "wf_slc_furni_signal":
                selected.FurniIds.UnionWith(world.Furni.Where(x => (input.IncludeWired || !x.IsWired) && input.Signal.FurniIds.Contains(x.Id)).Select(x => x.Id));
                break;
            case "wf_slc_users_signal":
                selected.UserIds.UnionWith(world.Users.Where(x => input.Signal.UserIds.Contains(x.Id)).Select(x => x.Id));
                break;
            case "wf_slc_furni_bytype": {
                    var source = P(0) switch { 0 => 100, 1 => 201, 2 => 0, _ => throw new ArgumentException("Unknown type source") };
                    var matchState = P(1) != 0;
                    var kinds = Furni(source).Select(x => (x.DefinitionId, State: matchState ? x.State : "")).ToHashSet();

                    foreach (var item in available) {
                        if (kinds.Contains((item.DefinitionId, matchState ? item.State : ""))) {
                            selected.FurniIds.Add(item.Id);
                        }
                    }

                    break;
                }
            case "wf_slc_users_bytype":
                selected.UserIds.UnionWith(world.Users.Where(x => ((int)x.Kind & P(0, 1)) != 0).Select(x => x.Id));
                break;
            case "wf_slc_users_team":
                selected.UserIds.UnionWith(world.Users.Where(x => x.Kind == WiredSelectorEntityKind.Player
                    && x.Team != 0 && (P(0) == 0 || x.Team == P(0))).Select(x => x.Id));
                break;
            case "wf_slc_users_byname": {
                    var names = c.Text.Split(['\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    selected.UserIds.UnionWith(world.Users.Where(x => names.Contains(x.Name)).Select(x => x.Id));
                    break;
                }
            case "wf_slc_users_handitem":
                selected.UserIds.UnionWith(world.Users.Where(x => P(0) == 0 ? x.HandItem != 0 : x.HandItem == P(0)).Select(x => x.Id));
                break;
            case "wf_slc_users_group": {
                    var groupId = P(0) == 0 ? world.RoomGroupId : P(1);

                    if (groupId > 0) {
                        selected.UserIds.UnionWith(world.Users.Where(x => x.Kind == WiredSelectorEntityKind.Player
                        && x.EquippedGroupId == groupId).Select(x => x.Id));
                    }

                    break;
                }
            case "wf_slc_users_byaction":
                selected.UserIds.UnionWith(world.Users.Where(x => MatchesAction(x, c, input)).Select(x => x.Id));
                break;
            case "wf_slc_furni_area":
            case "wf_slc_users_area": {
                    // Compare coordinates rather than iterate an unchecked client-sized rectangle.
                    bool InArea(int x, int y) => x >= P(0) && y >= P(1) && x < (long)P(0) + P(2)
                        && y < (long)P(1) + P(3) && x >= 0 && y >= 0 && x < world.Width && y < world.Height;

                    if (target == WiredSelectorTarget.Furni) {
                        selected.FurniIds.UnionWith(available.Where(x => x.Tiles.Any(t => InArea(t.X, t.Y))).Select(x => x.Id));
                    }
                    else {
                        selected.UserIds.UnionWith(world.Users.Where(x => InArea(x.X, x.Y)).Select(x => x.Id));
                    }

                    break;
                }
            case "wf_slc_furni_altitude": {
                    if (!double.TryParse(string.IsNullOrWhiteSpace(c.Text) ? "0" : c.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var altitude) || !double.IsFinite(altitude)) {
                        throw new ArgumentException("Altitude must be a finite invariant number");
                    }

                    selected.FurniIds.UnionWith(available.Where(x => (P(0, 1) switch
                    {
                        0 => x.Z < altitude,
                        1 => x.Z == altitude,
                        2 => x.Z > altitude,
                        _ => false
                    })).Select(x => x.Id));
                    break;
                }
            case "wf_slc_furni_onfurni":
            case "wf_slc_users_onfurni": {
                    var sourceIndex = target == WiredSelectorTarget.Furni ? 1 : 0;
                    var source = P(sourceIndex);

                    foreach (var item in Furni(source).Where(x => x.IsFloor)) {
                        if (target == WiredSelectorTarget.User) {
                            selected.UserIds.UnionWith(world.Users.Where(x => item.Tiles.Contains((x.X, x.Y))).Select(x => x.Id));
                            continue;
                        }

                        foreach (var other in available.Where(x => (P(0) == 3 || x.Id != item.Id) && x.Tiles.Contains((item.X, item.Y)))) {
                            if (P(0) switch
                            {
                                0 => other.Z > item.Z,
                                1 => other.Z < item.Z,
                                2 => other.Z == item.Z,
                                3 => true,
                                _ => false
                            }) {
                                selected.FurniIds.Add(other.Id);
                            }
                        }
                    }

                    break;
                }
            case "wf_slc_furni_neighborhood":
            case "wf_slc_users_neighborhood": {
                    var tiles = Neighborhood(c, world, input);

                    if (target == WiredSelectorTarget.Furni) {
                        selected.FurniIds.UnionWith(available.Where(x => x.Tiles.Any(tiles.Contains)).Select(x => x.Id));
                    }
                    else {
                        selected.UserIds.UnionWith(world.Users.Where(x => tiles.Contains((x.X, x.Y))).Select(x => x.Id));
                    }

                    break;
                }
            case "wf_slc_furni_with_var":
                if (input.FurniVariablePredicate is null && input.ScopedFurniVariablePredicate is null) {
                    throw new InvalidOperationException("Furniture variable predicate is required");
                }

                selected.FurniIds.UnionWith(available.Where(x => input.ScopedFurniVariablePredicate?.Invoke(name, c, x.Id, input) ?? input.FurniVariablePredicate!(name, c, x.Id)).Select(x => x.Id));
                break;
            case "wf_slc_users_with_var":
                if (input.UserVariablePredicate is null && input.ScopedUserVariablePredicate is null) {
                    throw new InvalidOperationException("Avatar variable predicate is required");
                }

                selected.UserIds.UnionWith(world.Users.Where(x => input.ScopedUserVariablePredicate?.Invoke(name, c, x.Id, input) ?? input.UserVariablePredicate!(name, c, x.Id)).Select(x => x.Id));
                break;
            case "wf_slc_remote": {
                    var stacks = RemoteStacks(c, world, input).ToList();

                    if (c.IntParams.Length >= 4 && P(3) > 0 && P(3) < stacks.Count) {
                        // Sample stacks once; all selectors on each chosen tile participate.
                        for (var i = stacks.Count - 1; i > 0; i--) {
                            var other = remoteRandom.Next(i + 1);
                            (stacks[i], stacks[other]) = (stacks[other], stacks[i]);
                        }

                        stacks = stacks.Take(P(3)).ToList();
                    }

                    var legacyPool = new WiredSelectedIds();
                    var legacyFurniModified = false;
                    var legacyUsersModified = false;
                    var firstStack = true;

                    foreach (var stack in stacks) {
                        if (visiting.Count >= 20 || !visiting.Add(stack[0])) {
                            continue;
                        }

                        // Referenced stacks have their own selector sequence, including filter/invert.
                        var stackPool = c.IntParams.Length == 2 ? legacyPool : new WiredSelectedIds();
                        var furniModified = c.IntParams.Length == 2 && legacyFurniModified;
                        var usersModified = c.IntParams.Length == 2 && legacyUsersModified;

                        try {
                            foreach (var id in stack) {
                                var remote = world.RemoteSelectors![id];
                                var remoteInput = input with
                                {
                                    SelectorPool = stackPool,
                                    FurniModified = furniModified,
                                    UsersModified = usersModified
                                };
                                var result = SelectRaw(remote.Name, remote.Configuration, world, remoteInput, visiting, remoteRandom);
                                stackPool = Compose(result, world, remoteInput);
                                furniModified |= result.Target is WiredSelectorTarget.Furni or WiredSelectorTarget.Both;
                                usersModified |= result.Target is WiredSelectorTarget.User or WiredSelectorTarget.Both;
                            }
                        }
                        finally {
                            visiting.Remove(stack[0]);
                        }

                        if (c.IntParams.Length == 2) {
                            legacyPool = stackPool;
                            legacyFurniModified = furniModified;
                            legacyUsersModified = usersModified;
                        }
                        else if (firstStack || P(2) == 0) {
                            selected.FurniIds.UnionWith(stackPool.FurniIds);
                            selected.UserIds.UnionWith(stackPool.UserIds);
                        }
                        else {
                            selected.FurniIds.IntersectWith(stackPool.FurniIds);
                            selected.UserIds.IntersectWith(stackPool.UserIds);
                        }

                        firstStack = false;
                    }

                    if (c.IntParams.Length == 2) {
                        selected.FurniIds.UnionWith(legacyPool.FurniIds);
                        selected.UserIds.UnionWith(legacyPool.UserIds);
                    }

                    break;
                }
        }

        return new(selected, target, filter, invert);
    }

    internal static IReadOnlyList<IReadOnlyList<uint>> RemoteStacks(WiredConfiguration c, WiredSelectorWorld world, WiredSelectorInputs? input = null)
    {
        var stacks = new List<IReadOnlyList<uint>>();
        var seen = new HashSet<(int X, int Y, uint UnprojectedId)>();
        var source = c.IntParams.Length == 5 ? c.IntParams[4] : WiredSources.Selected;
        var pickedIds = input is null ? c.SelectedItems : WiredSelectorSources.Furni(source, c, input, world);

        foreach (var pickedId in pickedIds) {
            var picked = world.Furni.FirstOrDefault(item => item.Id == pickedId && item.IsFloor);

            if (picked is null) {
                // Custom projections can supply a selector without a furniture model.
                if (world.RemoteSelectors?.ContainsKey(pickedId) == true && seen.Add((0, 0, pickedId))) {
                    stacks.Add(new[] { pickedId });
                }

                continue;
            }

            if (!seen.Add((picked.X, picked.Y, 0))) {
                continue;
            }

            var selectors = world.Furni.Where(item => item.IsFloor && item.X == picked.X && item.Y == picked.Y
                && world.RemoteSelectors?.ContainsKey(item.Id) == true)
                .OrderBy(item => item.Z).ThenBy(item => item.Id).Select(item => item.Id).ToArray();

            if (selectors.Length > 0) {
                stacks.Add(selectors);
            }
        }

        return stacks;
    }

    public static WiredSelectedIds Compose(WiredSelectorResult result, WiredSelectorWorld world, WiredSelectorInputs input)
    {
        var output = input.SelectorPool.Copy();

        if (result.Target is WiredSelectorTarget.Furni or WiredSelectorTarget.Both) {
            var kept = result.Invert ? world.Furni.Where(x => x.IsFloor && (input.IncludeWired || !x.IsWired))
                .Select(x => x.Id).Except(result.Selection.FurniIds) : result.Selection.FurniIds;

            if (result.FiltersExisting) {
                var basis = input.FurniModified ? input.SelectorPool.FurniIds : input.Triggering.FurniIds;
                output.FurniIds.Clear();
                output.FurniIds.UnionWith(basis.Intersect(kept));
            }
            else {
                output.FurniIds.UnionWith(kept);
            }
        }

        if (result.Target is WiredSelectorTarget.User or WiredSelectorTarget.Both) {
            var kept = result.Invert ? world.Users.Select(x => x.Id).Except(result.Selection.UserIds) : result.Selection.UserIds;

            if (result.FiltersExisting) {
                var basis = input.UsersModified ? input.SelectorPool.UserIds : input.Triggering.UserIds;
                output.UserIds.Clear();
                output.UserIds.UnionWith(basis.Intersect(kept));
            }
            else {
                output.UserIds.UnionWith(kept);
            }
        }

        return output;
    }

    private static HashSet<(int X, int Y)> Neighborhood(WiredConfiguration c, WiredSelectorWorld world, WiredSelectorInputs input)
    {
        int P(int i) => WiredSelectorSources.Param(c, i);
        var source = P(0);
        IEnumerable<(int X, int Y)> positions = source switch
        {
            0 or 1 or 2 => world.Users.Where(x => WiredSelectorSources.Users(source switch { 0 => 0, 1 => 201, _ => 11 }, input, world).Contains(x.Id)).Select(x => (x.X, x.Y)),
            3 or 4 or 5 => world.Furni.Where(x => x.IsFloor && WiredSelectorSources.Furni(source switch { 3 => 0, 4 => 100, _ => 201 }, c, input, world).Contains(x.Id)).Select(x => (x.X, x.Y)),
            _ => throw new ArgumentException("Unknown neighborhood source")
        };
        var offsets = new List<(int X, int Y)>();
        var count = P(5);

        if (count < 0 || count > WiredConfigurationLimits.NeighborhoodTiles || c.IntParams.Length < 6 + count * 2 && count > 0) {
            throw new ArgumentException("Neighborhood offsets are incomplete or exceed 81 tiles");
        }

        for (var i = 0; i < count; i++) {
            offsets.Add((P(6 + i * 2), P(7 + i * 2)));
        }

        var tiles = new HashSet<(int X, int Y)>();

        foreach (var (x, y) in positions) {
            foreach (var (dx, dy) in offsets) {
                var tx = (long)x + dx - P(3);
                var ty = (long)y + dy - P(4);

                if (tx >= 0 && ty >= 0 && tx < world.Width && ty < world.Height) {
                    tiles.Add(((int)tx, (int)ty));
                }
            }
        }

        return tiles;
    }

    private static bool MatchesAction(WiredSelectorAvatar user, WiredConfiguration c, WiredSelectorInputs input)
    {
        int P(int i, int fallback = 0) => WiredSelectorSources.Param(c, i, fallback);
        var wanted = P(0, 1);

        if (wanted == 4) {
            return !user.Idle;
        }

        if (wanted == 7) {
            return !user.Sitting && !user.Lying;
        }

        bool Match(int action, int value) => action == wanted && (wanted switch
        {
            9 => P(1) == 0 || value == P(2),
            10 => P(3) == 0 || value == P(4, 1),
            _ => true
        });

        if (input.ActionUserId == user.Id && input.Action is int action && Match(action, input.ActionParameter)) {
            return true;
        }

        if (wanted switch
        {
            5 => user.Idle,
            6 => user.Sitting,
            8 => user.Lying,
            9 => user.Sign is int sign && Match(9, sign),
            10 => user.Dance > 0 && Match(10, user.Dance),
            _ => false
        }) {
            return true;
        }

        return user.LastAction is int previous && input.NowMs >= user.LastActionAtMs
            && input.NowMs - user.LastActionAtMs <= 5000 && Match(previous, user.LastActionParameter);
    }

    internal static (int Filter, int Invert) SwitchIndexes(string name) => name switch
    {
        "wf_slc_furni_area" or "wf_slc_users_area" => (4, 5),
        "wf_slc_furni_bytype" or "wf_slc_furni_onfurni" or "wf_slc_users_group" => (2, 3),
        "wf_slc_users_byaction" => (5, 6),
        "wf_slc_furni_picks" or "wf_slc_furni_signal" or "wf_slc_users_signal" or "wf_slc_users_byname" or "wf_slc_remote" => (0, 1),
        "wf_slc_furni_with_var" or "wf_slc_users_with_var" => (7, 8),
        _ => (1, 2)
    };
}
