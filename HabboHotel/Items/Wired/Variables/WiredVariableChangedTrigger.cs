using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed class WiredVariableChangedTrigger : WiredModernBox, IWiredContextualTrigger
{
    public WiredVariableChangedTrigger(Room room, Item item, WiredBoxDescriptor descriptor) : base(room, item, descriptor) =>
        ApplyConfiguration(new() { IntParams = [0, 1, 1, 1, 1, 1, 1, -1] });
    public IReadOnlyCollection<WiredEventKind> Events { get; } = [WiredEventKind.Variable];
    public bool HidesChat(WiredRuntimeContext context) => false;
    public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Choose a scalar variable and change types.";
        var p = proposed.IntParams;
        var internalToken = proposed.Text.StartsWith("internal:@", StringComparison.Ordinal) && proposed.Text.Length > 10;

        if (proposed.Version != 1 || p.Length != 8 || !Enum.IsDefined((WiredVariableTarget)p[0])
            || p.Skip(1).Take(6).Any(value => value is not (0 or 1)) || proposed.Text.Length > 64
            || !internalToken && !WiredVariableModule.TryDefinitionId(proposed.Text, out _)
            || proposed.Text.IndexOfAny(['\t', '\r', '\n']) >= 0) {
            return false;
        }

        if (p[2] == 0) {
            p = p.SetItem(3, 0).SetItem(4, 0).SetItem(5, 0);
        }

        if (p[0] == (int)WiredVariableTarget.Global || internalToken) {
            p = p.SetItem(1, 0).SetItem(6, 0);
        }

        p = p.SetItem(7, p[7] < 0 ? -1 : p[7] & 7);

        if (p[1] == 0 && p[6] == 0 && (p[2] == 0 || p[3] + p[4] + p[5] == 0)) {
            return false;
        }

        validated = proposed with { IntParams = p };
        error = "";

        return true;
    }
    public override bool Execute(WiredRuntimeContext context)
    {
        if (!ReferenceEquals(context.Room, Instance) || context.Event.Kind != WiredEventKind.Variable
            || context.Event.VariableChange is not { } change || change.RoomId != context.Room.Id
            || !TryValidateConfiguration(context.ConfigurationOf(this), out var config, out _)) {
            return false;
        }

        return Matches(config, change);
    }
    public static bool Matches(WiredConfiguration config, WiredVariableChange change)
    {
        var p = config.IntParams;

        if (p.Length != 8 || p[0] != (int)change.Key.Target) {
            return false;
        }

        if (config.Text.StartsWith("internal:", StringComparison.Ordinal)) {
            if (RoomWiredBuiltinVariables.Normalize(config.Text) != change.InternalKey) {
                return false;
            }
        }
        else if (change.InternalKey.Length != 0 || !WiredVariableModule.TryDefinitionId(config.Text, out var id) || id != change.Key.DefinitionId) {
            return false;
        }

        var origin = change.Origin is >= 0 and <= 2 ? change.Origin : 0;

        if (p[7] is not (-1 or 0) && (p[7] & (1 << origin)) == 0) {
            return false;
        }

        if (change.Kind == WiredVariableChangeKind.Created) {
            return p[1] == 1;
        }

        if (change.Kind == WiredVariableChangeKind.Removed) {
            return p[6] == 1;
        }

        if (p[2] == 0 || change.Before is null || change.After is null) {
            return false;
        }

        return change.After.Value.CompareTo(change.Before.Value) switch { > 0 => p[3] == 1, < 0 => p[4] == 1, _ => p[5] == 1 };
    }
}
