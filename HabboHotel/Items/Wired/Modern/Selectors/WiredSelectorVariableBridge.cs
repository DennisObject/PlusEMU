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
        if (name is "wf_slc_furni_with_var" or "wf_slc_users_with_var")
        {
            return true;
        }

        if (name != "wf_slc_remote")
        {
            return false;
        }

        foreach (var id in configuration.SelectedItems)
        {
            if (visiting.Count >= 20 || !visiting.Add(id))
            {
                continue;
            }

            try
            {
                if (world.RemoteSelectors?.TryGetValue(id, out var remote) == true
                    && RequiresQueries(remote.Name, remote.Configuration, world, visiting))
                {
                    return true;
                }
            }
            finally { visiting.Remove(id); }
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

        return new(
            (name, configuration, id) => furni.TryGetValue(id, out var holder) && session.MatchSelector(name, configuration, holder),
            (name, configuration, id) => users.TryGetValue(id, out var holder) && session.MatchSelector(name, configuration, holder),
            request => session.ReadOperand((WiredVariableTarget)request.Target, request.Token,
                request.UserSource, request.FurniSource, request.Configuration),
            session.Dispose);
    }
}
