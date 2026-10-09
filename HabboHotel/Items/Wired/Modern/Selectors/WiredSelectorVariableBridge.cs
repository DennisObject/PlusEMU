using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;

namespace Plus.HabboHotel.Items.Wired.Modern.Selectors;

public static class WiredSelectorVariableBridge
{
    public static bool RequiresQueries(string name, WiredConfiguration configuration, WiredSelectorWorld world) =>
        RequiresQueries(name, configuration, world, []);

    private static bool RequiresQueries(string name, WiredConfiguration configuration, WiredSelectorWorld world,
        HashSet<uint> visiting)
    {
        if (name is "wf_slc_furni_with_var" or "wf_slc_users_with_var") {
            return true;
        }

        if (name != "wf_slc_remote") {
            return false;
        }

        if (configuration.IntParams.Length >= 5 && configuration.IntParams[4] != WiredSources.Selected) {
            // Dynamic sources can refer to stacks selected by an earlier referenced selector.
            return world.RemoteSelectors?.Values.Any(remote => remote.Name is "wf_slc_furni_with_var" or "wf_slc_users_with_var") == true;
        }

        foreach (var stack in WiredSelectorModule.RemoteStacks(configuration, world)) {
            if (visiting.Count >= 20 || !visiting.Add(stack[0])) {
                continue;
            }

            try {
                foreach (var id in stack) {
                    var remote = world.RemoteSelectors![id];

                    if (RequiresQueries(remote.Name, remote.Configuration, world, visiting)) {
                        return true;
                    }
                }
            }
            finally {
                visiting.Remove(stack[0]);
            }
        }

        return false;
    }

    public static WiredSelectorVariableQueries Create(WiredRuntimeContext context, WiredVariableModule module) =>
        Create(module, WiredVariableRuntimeFrames.Create(context, context.VariableFrame));

    public static WiredSelectorVariableQueries Create(WiredVariableModule module, WiredVariableFrame frame)
    {
        var session = new WiredVariableQueries(module, frame);
        var users = frame.Holders.Where(x => x.Target == WiredVariableTarget.User).ToDictionary(x => x.EntityId);
        var furni = frame.Holders.Where(x => x.Target == WiredVariableTarget.Furni && x.EntityId > 0)
            .ToDictionary(x => (uint)x.EntityId);

        var scoped = new Dictionary<WiredSelectorInputs, WiredVariableQueries>(ReferenceEqualityComparer.Instance);
        var disposed = false;
        WiredVariableQueries ForSelection(WiredSelectorInputs selection)
        {
            ObjectDisposedException.ThrowIf(disposed, session);

            if (scoped.TryGetValue(selection, out var existing)) {
                return existing;
            }

            IEnumerable<WiredVariableHolder> Selected(WiredVariableTarget target)
            {
                var live = frame.ResolveSource?.Invoke(target, WiredSources.AllRoom, []) ?? frame.Holders;
                var holders = live.Where(holder => holder.Target == target).ToDictionary(holder => holder.EntityId);
                var ids = target == WiredVariableTarget.Furni
                    ? selection.SelectorPool.FurniIds.Select(id => unchecked((int)id))
                    : selection.SelectorPool.UserIds;

                return ids.Where(holders.ContainsKey).Select(id => holders[id]);
            }
            var selectedFrame = new WiredVariableFrame(frame.RoomId, frame.Holders)
            {
                RuntimeContext = frame.RuntimeContext,
                Context = frame.Context,
                VariableChanges = frame.VariableChanges,
                Trigger = frame.Trigger,
                Signal = frame.Signal,
                Depth = frame.Depth,
                ChatText = frame.ChatText,
                ResolveSource = (target, source, picked) => source == WiredSources.Selector ? Selected(target)
                    : WiredVariableExecutors.Select(frame, target, source, source, picked)
            };
            selectedFrame.Selector.AddRange(Selected(WiredVariableTarget.Furni).Concat(Selected(WiredVariableTarget.User)));
            var query = new WiredVariableQueries(module, selectedFrame);
            scoped[selection] = query;

            return query;
        }
        void Dispose()
        {
            disposed = true;
            session.Dispose();

            foreach (var query in scoped.Values) {
                query.Dispose();
            }
        }

        return new(
            (name, configuration, id) => furni.TryGetValue(id, out var holder) && session.MatchSelector(name, configuration, holder),
            (name, configuration, id) => users.TryGetValue(id, out var holder) && session.MatchSelector(name, configuration, holder),
            request => request.UseSelected ? session.ReadSelectedOperand((WiredVariableTarget)request.Target, request.Token)
                : session.ReadOperand((WiredVariableTarget)request.Target, request.Token,
                request.UserSource, request.FurniSource, request.Configuration),
            Dispose,
            (name, configuration, id, selection) => furni.TryGetValue(id, out var holder)
                && ForSelection(selection).MatchSelector(name, configuration, holder),
            (name, configuration, id, selection) => users.TryGetValue(id, out var holder)
                && ForSelection(selection).MatchSelector(name, configuration, holder));
    }
}
