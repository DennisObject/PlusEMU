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
        Configuration = WiredNativeEditorProjection.DefaultRuntime(item.Id, descriptor) ?? new();
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
        if (!WiredNativeEditorProjection.IsBound(Item.Id, Descriptor, validated)) {
            throw new InvalidDataException("Unbound or altered native Wired runtime projection.");
        }

        Configuration = validated;
        StringData = validated.Text;
        ItemsData = string.Join(';', validated.SelectedItems);
    }
    public bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid variable addon configuration.";

        if (!WiredNativeEditorProjection.IsBound(Item.Id, Descriptor, proposed)
            || !WiredNativeEditorProjection.TryValidateRuntime(Item, Descriptor, proposed, out _, out error)) {
            return false;
        }

        error = "Invalid variable addon configuration.";

        return TryDecode(proposed, null, out _);
    }

    /// <summary>
    /// Shape check, and with an authority the current local tokens of the picked variables: the variable shown or filtered
    /// by and, for a variable-valued count, the operand. Unresolved ids give empty tokens, which the caller treats as inactive.
    /// </summary>
    private bool TryDecode(WiredConfiguration c, WiredVariableModule? authority, out (string Variable, string Operand) tokens)
    {
        tokens = ("", "");
        var p = c.IntParams;
        var text = c.Text.Split('\t');
        var output = Descriptor.CanonicalName == "wf_xtra_text_output_variable";
        var ids = c.VariableIds;

        if (c.Version != 1 || c.Text.Length > 5000 || ids.Length != (output ? 1 : 2)) {
            return false;
        }

        bool Resolve(string id, int target, out string token)
        {
            token = "";

            if (WiredVariableAbsent.Is(id)) {
                return true;
            }

            if (!WiredVariableDescription.TryParseCatalogId(id, out var parsed, out var parsedToken) || (int)parsed != target) {
                return false;
            }

            token = authority is null ? parsedToken : authority.TryResolveCatalogId(id, parsed, out var reference) ? reference.Token : "";

            return true;
        }

        if (output) {
            if (p.Length != 5 || !Enum.IsDefined((WiredVariableTarget)p[0]) || p[1] is not (1 or 2) || p[2] is not (1 or 2)
                || !Source(p[3]) || !Source(p[4]) || text.Length != 3 || text[0].Length != 0 || text[1].Length is < 1 or > 32 || text[2].Length > 16
                || text[1].Contains('\n') || text[1].Contains('\r') || text[2].Contains('\n') || text[2].Contains('\r')) {
                return false;
            }

            return Resolve(ids[0], p[0], out tokens.Variable);
        }

        var target = (int)(Descriptor.CanonicalName == "wf_xtra_filter_users_by_var" ? WiredVariableTarget.User : WiredVariableTarget.Furni);

        if (p.Length != 6 || p[0] is < 0 or > 5 || p[1] is not (0 or 1) || p[2] is < 0 or > 10000
            || !Enum.IsDefined((WiredVariableTarget)p[3]) || !Source(p[4]) || !Source(p[5]) || c.Text.Length != 0) {
            return false;
        }

        // A literal count ignores the operand slot.
        return Resolve(ids[0], target, out tokens.Variable) && (p[1] == 0 || Resolve(ids[1], p[3], out tokens.Operand));
    }
    public bool Apply(WiredRuntimeContext context)
    {
        var configuration = context.ConfigurationOf(this);

        if (!ReferenceEquals(context.Room, Instance) || !TryValidateConfiguration(configuration, out _, out _)
            || !TryDecode(configuration, _variables, out var resolved)) {
            return false;
        }

        if (Descriptor.CanonicalName == "wf_xtra_text_output_variable") {
            context.Policy.TextFormatters.Add((current, text) => Format(current, configuration, resolved.Variable, text));

            return true;
        }

        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        var frame = context.VariableFrame;
        var p = configuration.IntParams;
        var target = Descriptor.CanonicalName == "wf_xtra_filter_users_by_var" ? WiredVariableTarget.User : WiredVariableTarget.Furni;
        var count = p[2];

        // A filter on a variable that no longer resolves keeps nothing rather than everything.
        if (resolved.Variable.Length == 0) {
            return true;
        }

        if (p[1] == 1) {
            using var queries = new WiredVariableQueries(_variables, frame);
            count = (int)Math.Clamp(queries.ReadOperand((WiredVariableTarget)p[3], configuration.VariableIds[1], p[4], p[5], configuration) ?? 1, 0, 10000);
        }

        var holders = frame.Holders.Where(holder => holder.Target == target).ToDictionary(holder => holder.EntityId);
        var orderedIds = target == WiredVariableTarget.User ? context.SelectorUserOrder
            : context.SelectorFurniOrder.Select(id => unchecked((int)id));
        var candidates = orderedIds.Where(holders.ContainsKey).Select(id => holders[id]);
        var kept = WiredVariablePredicates.Filter(_variables, new(target, resolved.Variable), candidates, frame, p[0], count);

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
    private string Format(WiredRuntimeContext context, WiredConfiguration configuration, string variable, string text)
    {
        var p = configuration.IntParams;
        var tokens = configuration.Text.Split('\t');
        var placeholder = "$(" + tokens[1] + ")";

        if (variable.Length == 0 || !text.Contains(placeholder, StringComparison.Ordinal)) {
            return text;
        }

        context.VariableFrame = WiredVariableRuntimeFrames.Create(context, context.VariableFrame);
        var frame = context.VariableFrame;
        var reference = new WiredVariableReference((WiredVariableTarget)p[0], variable);
        using var reads = _variables.CaptureReads([reference], frame);
        var labels = p[1] == 2 && WiredVariableModule.TryDefinitionId(variable, out var id) ? _textConnector(id) : null;
        var values = WiredVariableExecutors.Select(frame, reference.Target, p[3], p[4], configuration.SelectedItems)
            .Select(holder => reads.Read(reference, holder, frame)).OfType<WiredVariableValue>()
            .Select(value => value.Value is >= int.MinValue and <= int.MaxValue && labels?.GetValueOrDefault((int)value.Value) is { } label ? label : value.Value.ToString(CultureInfo.InvariantCulture));
        var replacement = p[2] == 2 ? string.Join(tokens[2], values) : values.FirstOrDefault() ?? "";

        return text.Replace(placeholder, replacement, StringComparison.Ordinal);
    }
    private static bool Source(int source) => source is 0 or 11 or 100 or 101 or 200 or 201;
}
