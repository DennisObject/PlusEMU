using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Chests;

// Text transports Octane's extra source/variable/selection slots, without changing the legacy envelope.
public sealed record WiredChestEditorData
{
    public string Text { get; init; } = "";
    public string[] Variables { get; init; } = [];
    public int[] FurniSources { get; init; } = [];
    public int[] UserSources { get; init; } = [];
    public uint[][] Picks { get; init; } = [];
    public static WiredChestEditorData Parse(string text) => text.StartsWith("@chest:", StringComparison.Ordinal)
        ? JsonSerializer.Deserialize<WiredChestEditorData>(text[7..]) ?? throw new InvalidDataException("Missing chest editor fields.")
        : new() { Text = text };
}

public class WiredChestBox : WiredModernBox
{
    protected readonly WiredChestRoom Chests;
    private static readonly ConditionalWeakTable<WiredExecutionPolicy, ContractSource> Custom = new();
    public static readonly string[] Names = ["wf_act_give_currency", "wf_act_give_furni", "wf_act_init_transaction", "wf_act_cancel_transaction",
        "wf_cnd_chest_has_items", "wf_cnd_chest_has_item_type", "wf_trg_transaction_complete", "wf_trg_transaction_fail",
        "wf_xtra_custom_contract", "wf_xtra_scan_chest_furni_by_type"];

    public WiredChestBox(Room room, Item item, WiredBoxDescriptor descriptor, WiredChestRoom chests) : base(room, item, descriptor)
    {
        Chests = chests;
        TryValidateConfiguration(Defaults(descriptor.CanonicalName), out var defaults, out _);
        ApplyConfiguration(defaults);
    }
    public static IWiredConfiguredItem Create(Room room, Item item, WiredBoxDescriptor descriptor, WiredChestRoom chests) => descriptor.Category switch
    {
        WiredBoxCategory.Action => new WiredChestAction(room, item, descriptor, chests),
        WiredBoxCategory.Addon => new WiredChestAddon(room, item, descriptor, chests),
        WiredBoxCategory.Trigger => new WiredChestTrigger(room, item, descriptor, chests),
        _ => new WiredChestBox(room, item, descriptor, chests)
    };
    public static WiredConfiguration Defaults(string name) => new()
    {
        IntParams = name switch
        {
            "wf_act_give_currency" => [0, 1, 0, 0, 1, 11],
            "wf_act_give_furni" => [0, 1, 0, 0, 0, 0],
            "wf_act_init_transaction" => [0, 10, 0, 0, 0, 300],
            "wf_act_cancel_transaction" or "wf_xtra_scan_chest_furni_by_type" => [0],
            "wf_cnd_chest_has_items" or "wf_cnd_chest_has_item_type" => [0, 0, 1, 2],
            "wf_xtra_custom_contract" => [0, 0, 0, 1, 0, 0, 0, 0, 1, 0],
            _ => []
        }
    };
    public override bool TryValidateConfiguration(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed;
        error = "Invalid chest Wired configuration.";

        if (!WiredLegacyProtocol.IsWithinLimits(proposed)) {
            return false;
        }

        var p = proposed.IntParams;
        bool B(int i) => p[i] is 0 or 1;
        bool T(int i) => p[i] is >= 0 and <= 3;
        var valid = Descriptor.CanonicalName switch
        {
            "wf_act_give_currency" or "wf_act_give_furni" => p.Length == 6 && B(0) && p[1] >= 1 && B(2) && T(3) && B(4)
                && (Descriptor.CanonicalName == "wf_act_give_currency" ? p[5] is 11 or 13 : p[5] is >= 0 and <= 2),
            "wf_act_init_transaction" => p.Length == 6 && p[0] is >= 0 and <= 2 && p[1] is >= 1 and <= 500 && B(2) && T(3) && B(4) && p[5] is >= 30 and <= 3600,
            "wf_act_cancel_transaction" or "wf_xtra_scan_chest_furni_by_type" => p.Length == 1 && B(0),
            "wf_cnd_chest_has_items" or "wf_cnd_chest_has_item_type" => p.Length == 4 && p[0] is >= 0 and <= 1000000 && B(1) && T(2) && p[3] is >= 0 and <= 5,
            "wf_trg_transaction_complete" or "wf_trg_transaction_fail" => p.Length == 0,
            "wf_xtra_custom_contract" => p.Length == 10 && B(0) && B(1) && B(2) && p[3] is >= 1 and <= 100000 && T(4)
                && B(5) && B(6) && B(7) && p[8] is >= 1 and <= 100000 && T(9),
            _ => false
        };

        if (!valid) {
            return false;
        }

        try {
            var data = WiredChestEditorData.Parse(proposed.Text);

            if (data.Variables.Length > 2 || data.Variables.Any(token => token == null || token.Length > 1024)
                || data.Text.Length > 200 || data.FurniSources.Length > 4 || data.UserSources.Length > 2 || data.Picks.Length > 4
                || data.FurniSources.Any(source => source is not (0 or 100 or 200 or 201))
                || data.UserSources.Any(source => source is not (0 or 11 or 200 or 201))
                || data.Picks.Any(picks => picks == null || picks.Length > 100 || picks.Any(id => id == 0))
                || data.Picks.SelectMany(picks => picks).Any(id => !proposed.SelectedItems.Contains(id))) {
                return false;
            }

            validated = proposed with { VariableIds = data.Variables.ToImmutableArray() };
            error = "";

            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or NullReferenceException) {
            return false;
        }
    }
    protected Item[] Furni(WiredRuntimeContext context, WiredConfiguration config, int slot)
    {
        var data = WiredChestEditorData.Parse(config.Text);
        var ids = slot < data.Picks.Length ? data.Picks[slot] : config.SelectedItems.ToArray();
        var source = slot < data.FurniSources.Length ? data.FurniSources[slot] : 100;

        return context.Targets.ResolveFurni(context, ids, source);
    }
    protected RoomUser[] Users(WiredRuntimeContext context, WiredConfiguration config, int slot = 0)
    {
        var data = WiredChestEditorData.Parse(config.Text);
        var source = slot < data.UserSources.Length ? data.UserSources[slot] : 0;

        return context.Targets.ResolveUsers(context, [], source).Where(user => !user.IsBot && user.GetClient() != null).ToArray();
    }
    protected long? Operand(WiredRuntimeContext context, WiredConfiguration config, int valueIndex, int variableIndex, int targetIndex, int variableSlot, int furniSlot, int userSlot)
    {
        if (config.IntParams[variableIndex] == 0) {
            return config.IntParams[valueIndex];
        }

        var data = WiredChestEditorData.Parse(config.Text);

        if (context.VariableFrame == null || variableSlot >= data.Variables.Length) {
            return null;
        }

        var target = (WiredVariableTarget)config.IntParams[targetIndex];
        var holders = target switch
        {
            WiredVariableTarget.Furni => Furni(context, config, furniSlot).Select(WiredVariableRuntimeFrames.FurniHolder),
            WiredVariableTarget.User => Users(context, config, userSlot).Select(WiredVariableRuntimeFrames.UserHolder),
            _ => [new WiredVariableHolder(target, 0, 0)]
        };

        foreach (var holder in holders) {
            var value = context.Room.GetWired().Variables.Module.Read(new(target, data.Variables[variableSlot]), holder, context.VariableFrame);

            if (value != null) {
                return value.Value;
            }
        }

        return null;
    }
    public override bool Execute(WiredRuntimeContext context)
    {
        var c = context.ConfigurationOf(this);
        var types = Descriptor.CanonicalName == "wf_cnd_chest_has_item_type" ? Furni(context, c, 0).Select(WiredChestItemType.Of).ToHashSet() : null;
        var slot = types == null ? 0 : 1;
        var chests = Chests.Read(Furni(context, c, slot));
        var reference = Operand(context, c, 0, 1, 2, 0, slot + 1, 0);
        var amount = types == null ? chests.Sum(chest => (long)chest.Amount)
            : chests.Sum(chest => (long)chest.Items.Count(item => types.Contains(WiredChestItemType.Of(item))));

        return chests.Length > 0 && reference is { } value && c.IntParams[3] switch
        {
            0 => amount < value,
            1 => amount == value,
            2 => amount > value,
            3 => amount <= value,
            4 => amount != value,
            5 => amount >= value,
            _ => false
        };
    }
    private sealed record ContractSource(WiredChestContract? Contract);
    protected static void SetCustom(WiredRuntimeContext context, WiredChestContract? contract)
    {
        Custom.Remove(context.Policy);
        Custom.Add(context.Policy, new(contract));
    }
    protected static bool TryCustom(WiredRuntimeContext context, out WiredChestContract? contract)
    {
        var found = Custom.TryGetValue(context.Policy, out var value);
        contract = value?.Contract;

        return found;
    }
}

public sealed class WiredChestAction(Room room, Item item, WiredBoxDescriptor descriptor, WiredChestRoom chests)
    : WiredChestBox(room, item, descriptor, chests), IWiredContextualAction
{
    public bool IsNegative => false;
    public override bool Execute(WiredRuntimeContext context)
    {
        var c = context.ConfigurationOf(this);
        var actors = Users(context, c);
        var picked = Furni(context, c, 0);

        if (Descriptor.CanonicalName == "wf_act_cancel_transaction") {
            return Chests.CancelByWired(actors, picked.Where(item => WiredChestFurniture.IsContract(item.Definition)).Select(item => item.Id).ToArray(), c.IntParams[0] == 1);
        }

        if (Descriptor.CanonicalName == "wf_act_init_transaction") {
            WiredChestContract? contract;
            var custom = TryCustom(context, out contract);
            var contractItem = custom ? null : Furni(context, c, 1).FirstOrDefault(item => WiredChestFurniture.IsContract(item.Definition));
            contract ??= contractItem == null ? null : Chests.Store.LoadContract(contractItem);
            var multiplier = c.IntParams[0] == 0 ? 1 : Operand(context, c, 1, 2, 3, 0, 2, 1);
            var changed = false;

            foreach (var actor in actors) {
                if (contract == null || multiplier == null) {
                    Chests.Fail(actor, contractItem?.Id ?? Item.Id, WiredChestFailure.Misconfig);
                }
                else {
                    changed |= Chests.Start(actor.GetClient()!, contractItem?.Id ?? Item.Id, picked, contract, c.IntParams[0],
                        (int)Math.Clamp(multiplier.Value, 1, 500), c.IntParams[4] == 1 ? c.IntParams[5] : 0);
                }
            }

            return changed;
        }

        var kind = Descriptor.CanonicalName == "wf_act_give_currency" ? WiredChestKind.Coins : WiredChestKind.Furni;
        var snapshots = Chests.Read(picked).Where(chest => chest.Kind == kind && chest.Usable).ToArray();
        var amount = c.IntParams[0] == 1 ? snapshots.Sum(chest => (long)chest.Amount) : Operand(context, c, 1, 2, 3, 0, 1, 1);

        if (snapshots.Length == 0 || amount is null or <= 0 or > int.MaxValue || c.IntParams[0] == 1 && actors.Length > 1) {
            return false;
        }

        var gave = false;

        foreach (var actor in actors) {
            var current = Chests.Read(picked).Where(chest => chest.Kind == kind && chest.Usable).ToArray();
            var boundedTake = Math.Min(amount.Value, current.Sum(chest => (long)chest.Amount));

            if (boundedTake > int.MaxValue) {
                return false;
            }

            var take = (int)boundedTake;

            if (take == 0) {
                continue;
            }

            var items = current.SelectMany(chest => chest.Items).ToArray();

            if (kind == WiredChestKind.Furni && c.IntParams[5] == 2) {
                Array.Reverse(items);
            }
            else if (kind == WiredChestKind.Furni && c.IntParams[5] == 0) {
                items = current.SelectMany(chest => chest.Items.OrderBy(item => chest.RandomKeys.GetValueOrDefault(item.Id)).ThenBy(item => item.Id)).ToArray();
            }

            var reward = kind == WiredChestKind.Coins ? new[] { new WiredChestNode(take) }
                : items.Take(take).GroupBy(WiredChestItemType.Of).Select(group => new WiredChestNode(group.Count(), group.Key)).ToArray();
            var result = Chests.Move(actor.GetClient()!, new()
            {
                RoomId = room.Id,
                UserId = actor.HabboId,
                SourceId = Item.Id,
                ChestIds = current.Select(chest => chest.Id).ToArray(),
                Wired = true,
                Reward = reward,
                Order = kind == WiredChestKind.Furni ? c.IntParams[5] : 1
            });

            if (result.Succeeded && !result.Replayed) {
                actor.GetClient()!.Send(new WiredChestRewardComposer(result, WiredChestEditorData.Parse(c.Text).Text, c.IntParams[4] == 1));
                gave = true;
            }
        }

        return gave;
    }
}

public sealed class WiredChestTrigger(Room room, Item item, WiredBoxDescriptor descriptor, WiredChestRoom chests)
    : WiredChestBox(room, item, descriptor, chests), IWiredContextualTrigger
{
    public IReadOnlyCollection<WiredEventKind> Events => [Descriptor.CanonicalName == "wf_trg_transaction_complete" ? WiredEventKind.TransactionComplete : WiredEventKind.TransactionFail];
    public bool HidesChat(WiredRuntimeContext context) => false;
    public override bool Execute(WiredRuntimeContext context) => Events.Contains(context.Event.Kind);
    public bool CanTrigger(WiredRuntimeContext context)
    {
        var selected = context.ConfigurationOf(this).SelectedItems;

        return selected.Length == 0 || context.Event.EventItem is { } source && selected.Contains(source.Id);
    }
}

public sealed class WiredChestAddon(Room room, Item item, WiredBoxDescriptor descriptor, WiredChestRoom chests)
    : WiredChestBox(room, item, descriptor, chests), IWiredContextualAddon
{
    public bool AfterConditions => true;
    public void Reset() { }
    public override bool Execute(WiredRuntimeContext context) => Apply(context);
    public bool Apply(WiredRuntimeContext context)
    {
        var c = context.ConfigurationOf(this);

        if (Descriptor.CanonicalName == "wf_xtra_scan_chest_furni_by_type") {
            var types = Furni(context, c, 0).Select(WiredChestItemType.Of).ToHashSet();
            var data = WiredChestEditorData.Parse(c.Text);
            var frame = context.VariableFrame;

            if (frame == null || data.Variables.Length == 0 || !data.Variables[0].StartsWith("custom:", StringComparison.Ordinal)
                || !uint.TryParse(data.Variables[0][7..], out var id)) {
                return false;
            }

            var count = Chests.Read(Furni(context, c, 1)).Sum(chest => c.IntParams[0] == 1
                ? WiredChestRoom.Preview(chest).Count(types.Contains) : chest.Items.Count(item => types.Contains(WiredChestItemType.Of(item))));

            return context.Room.GetWired().Variables.Module.CaptureContextValues(new Dictionary<uint, long> { [id] = count }, frame);
        }

        WiredChestNode? Node(int side)
        {
            var offset = side * 5;

            if (c.IntParams[offset] == 0) {
                return null;
            }

            var amount = Operand(context, c, offset + 3, offset + 2, offset + 4, side, side + 2, side);
            var furni = c.IntParams[offset + 1] == 1 ? Furni(context, c, side).FirstOrDefault() : null;

            return amount is < 1 or > 100000 || amount == null || c.IntParams[offset + 1] == 1 && furni == null
                ? null : new((int)amount.Value, furni == null ? null : WiredChestItemType.Of(furni));
        }

        var payment = Node(0);
        var reward = Node(1);
        var contract = payment == null && reward == null ? null : new WiredChestContract
        {
            Id = Item.Id,
            Kind = payment == null ? WiredContractKind.Reward : reward == null ? WiredContractKind.Payment : WiredContractKind.Trade,
            PaymentMode = 1,
            Payment = payment == null ? [] : [[payment]],
            Reward = reward == null ? null : [reward]
        };
        SetCustom(context, contract);

        return true;
    }
}
