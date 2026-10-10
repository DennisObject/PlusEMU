using System.Collections.Concurrent;
using System.Globalization;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Variables;

public sealed class WiredVariableAddonBox : IWiredContextualAddon
{
    private readonly WiredVariableModule _variables;
    private readonly Func<uint, IReadOnlyDictionary<int, string>> _textConnector;
    public WiredVariableAddonBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredVariableModule variables,
        Func<uint, IReadOnlyDictionary<int, string>> textConnector)
    {
        Instance = room;
        Item = item;
        Descriptor = descriptor with { Support = WiredBoxSupport.Implemented };
        _variables = variables;
        _textConnector = textConnector;
        Configuration = descriptor.CanonicalName == "wf_xtra_text_output_variable"
            ? new() { IntParams = [0, 1, 1, 0, 0], Text = "\tvalue\t, " }
            : new() { IntParams = [0, 0, 1, 0, 0, 0], Text = "\t" };
    }
    public static bool Supports(string name) => name is "wf_xtra_filter_furni_by_var" or "wf_xtra_filter_users_by_var" or "wf_xtra_text_output_variable";
    public Room Instance { get; set; }
    public Item Item { get; set; }
    public WiredBoxDescriptor Descriptor { get; }
    public WiredConfiguration Configuration { get; private set; }
    public WiredBoxType Type => WiredBoxType.None;
    public ConcurrentDictionary<uint, Item> SetItems { get; set; } = new();
    public string StringData { get; set; } = "";
    public string ItemsData { get; set; } = "";
    public bool BoolData { get; set; }
    public bool AfterConditions => false;
    public void Reset() { }
    public bool Execute(params object[] arguments) => false;
    public void HandleSave(IIncomingPacket packet) => throw new InvalidOperationException("Variable addons require validated persistence.");
    public void ApplyConfiguration(WiredConfiguration validated)
    {
        Configuration = validated;
        StringData = validated.Text;
        ItemsData = string.Join(';', validated.SelectedItems);
    }
    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid variable addon configuration.";
        var p = proposed.IntParams;
        var tokens = proposed.Text.Split('\t');

        if (proposed.Version != 1 || proposed.Text.Length > 5000 || !Token(tokens[0])) {
            return false;
        }

        if (Descriptor.CanonicalName == "wf_xtra_text_output_variable") {
            if (p.Length != 5 || !Enum.IsDefined((WiredVariableTarget)p[0]) || p[1] is not (1 or 2) || p[2] is not (1 or 2)
                || !Source(p[3]) || !Source(p[4]) || tokens.Length != 3 || tokens[1].Length is < 1 or > 32 || tokens[2].Length > 16
                || tokens[1].Contains('\n') || tokens[1].Contains('\r') || tokens[2].Contains('\n') || tokens[2].Contains('\r')) {
                return false;
            }
        }
        else if (p.Length != 6 || p[0] is < 0 or > 5 || p[1] is not (0 or 1) || p[2] is < 0 or > 10000
            || !Enum.IsDefined((WiredVariableTarget)p[3]) || !Source(p[4]) || !Source(p[5])
            || tokens.Length != 2 || p[1] == 1 && !Token(tokens[1])) {
            return false;
        }

        error = "";

        return true;
    }
    public bool Apply(WiredRuntimeContext context)
    {
        var configuration = context.ConfigurationOf(this);

        if (!ReferenceEquals(context.Room, Instance) || !TryValidateConfiguration(configuration, out _, out _)) {
            return false;
        }

        if (Descriptor.CanonicalName == "wf_xtra_text_output_variable") {
            context.Policy.TextFormatters.Add((current, text) => Format(current, configuration, text));

            return true;
        }

        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        var frame = context.VariableFrame;
        var p = configuration.IntParams;
        var tokens = configuration.Text.Split('\t');
        var target = Descriptor.CanonicalName == "wf_xtra_filter_users_by_var" ? WiredVariableTarget.User : WiredVariableTarget.Furni;
        var count = p[2];

        if (p[1] == 1) {
            using var queries = new WiredVariableQueries(_variables, frame);
            count = (int)Math.Clamp(queries.ReadOperand((WiredVariableTarget)p[3], tokens[1], p[4], p[5], configuration) ?? 1, 0, 10000);
        }

        var holders = frame.Holders.Where(holder => holder.Target == target).ToDictionary(holder => holder.EntityId);
        var orderedIds = target == WiredVariableTarget.User ? context.SelectorUserOrder
            : context.SelectorFurniOrder.Select(id => unchecked((int)id));
        var candidates = orderedIds.Where(holders.ContainsKey).Select(id => holders[id]);
        var kept = WiredVariablePredicates.Filter(_variables, new(target, tokens[0]), candidates, frame, p[0], count);

        if (target == WiredVariableTarget.User) {
            context.SelectorUserOrder.Clear();
            context.SelectorUserOrder.AddRange(kept.Select(x => x.EntityId));
            context.SelectorPool.UserIds.Clear();
            context.SelectorPool.UserIds.UnionWith(context.SelectorUserOrder);

            if (context.SelectorKinds.HasFlag(WiredSelectionKind.Users)) {
                context.Selected.UserIds.Clear();
                context.Selected.UserIds.UnionWith(context.SelectorUserOrder);
            }
        }
        else {
            context.SelectorFurniOrder.Clear();
            context.SelectorFurniOrder.AddRange(kept.Select(x => unchecked((uint)x.EntityId)));
            context.SelectorPool.FurniIds.Clear();
            context.SelectorPool.FurniIds.UnionWith(context.SelectorFurniOrder);

            if (context.SelectorKinds.HasFlag(WiredSelectionKind.Furni)) {
                context.Selected.FurniIds.Clear();
                context.Selected.FurniIds.UnionWith(context.SelectorFurniOrder);
            }
        }

        return true;
    }
    private string Format(WiredRuntimeContext context, WiredConfiguration configuration, string text)
    {
        var p = configuration.IntParams;
        var tokens = configuration.Text.Split('\t');
        var placeholder = "$(" + tokens[1] + ")";

        if (!text.Contains(placeholder, StringComparison.Ordinal)) {
            return text;
        }

        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        var frame = context.VariableFrame;
        var reference = new WiredVariableReference((WiredVariableTarget)p[0], tokens[0]);
        using var reads = _variables.CaptureReads([reference], frame);
        var labels = p[1] == 2 && WiredVariableModule.TryDefinitionId(tokens[0], out var id) ? _textConnector(id) : null;
        var values = WiredVariableExecutors.Select(frame, reference.Target, p[3], p[4], configuration.SelectedItems)
            .Select(holder => reads.Read(reference, holder, frame)).OfType<WiredVariableValue>()
            .Select(value => value.Value is >= int.MinValue and <= int.MaxValue && labels?.GetValueOrDefault((int)value.Value) is { } label ? label : value.Value.ToString(CultureInfo.InvariantCulture));
        var replacement = p[2] == 2 ? string.Join(tokens[2], values) : values.FirstOrDefault() ?? "";

        return text.Replace(placeholder, replacement, StringComparison.Ordinal);
    }
    private static bool Token(string value) => WiredVariableModule.TryDefinitionId(value, out _) || (value.StartsWith("internal:@", StringComparison.Ordinal) || value.StartsWith("internal:~", StringComparison.Ordinal)) && value.Length > 10;
    private static bool Source(int source) => source is 0 or 11 or 100 or 101 or 200 or 201;
}
