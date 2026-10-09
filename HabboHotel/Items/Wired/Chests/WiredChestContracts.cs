using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items.Wired.Chests;

public static class WiredChestContracts
{
    public static bool Valid(WiredChestContract contract, IItemDataManager definitions)
    {
        bool ValidNode(WiredChestNode node) => node.Amount >= 1 && (node.ItemType is { } type
            ? node.Amount <= 500 && definitions.Items.Values.Any(definition => definition.SpriteId == type.SpriteId
                && (definition.Type == ItemType.Wall) == type.Wall && definition.AllowTrade
                && definition.InteractionType != InteractionType.Exchange && !WiredChestFurniture.IsChest(definition) && !WiredChestFurniture.IsContract(definition))
            : node.Amount <= 100000);
        bool ValidRule(WiredChestNode[] rule) => rule.Length is >= 1 and <= 5 && rule.All(ValidNode);

        return contract.PaymentMode is >= 0 and <= 1 && contract.Layout is "generic" or "games"
            && contract.RewardCategory is 11 or 13 && contract.ReceiveText.Length <= 60 && contract.RewardText.Length <= 200
            && (contract.Payment == null || contract.Payment.Length <= 3 && contract.Payment.All(ValidRule))
            && (contract.Kind == WiredContractKind.Reward || contract.Kind == WiredContractKind.Payment && contract.PaymentMode == 0
                || contract.Payment is { Length: > 0 })
            && (contract.Kind == WiredContractKind.Payment || contract.Reward != null && ValidRule(contract.Reward));
    }

    public static bool CanOffer(WiredChestContract? contract, InventoryItem item) => contract == null
        || contract is { Kind: WiredContractKind.Payment, PaymentMode: 0 }
        || (contract.Payment ?? []).SelectMany(rule => rule).Any(node => item.Definition.InteractionType == InteractionType.Exchange
            ? node.ItemType == null : node.ItemType == WiredChestItemType.Of(item));

    public static int Times(WiredChestContract contract, IEnumerable<InventoryItem> offered, int mode, int cap) => Match(contract, offered.ToArray(), mode, cap).Times;

    private static (WiredChestNode[]? Rule, int Times) Match(WiredChestContract contract, InventoryItem[] items, int mode, int cap)
    {
        if (contract.Kind == WiredContractKind.Payment && contract.PaymentMode == 0) {
            return (null, items.Length > 0 ? 1 : 0);
        }

        var coins = items.Where(item => item.Definition.InteractionType == InteractionType.Exchange).Sum(item => (long)item.Definition.BehaviourData);
        var counts = items.Where(item => item.Definition.InteractionType != InteractionType.Exchange)
            .GroupBy(WiredChestItemType.Of).ToDictionary(group => group.Key, group => (long)group.Count());
        WiredChestNode[]? best = null;
        long bestTimes = 0;

        foreach (var rule in contract.Payment ?? []) {
            var requiredCoins = rule.Where(node => node.ItemType == null).Sum(node => (long)node.Amount);
            var requiredItems = rule.Where(node => node.ItemType != null).GroupBy(node => node.ItemType!.Value)
                .ToDictionary(group => group.Key, group => group.Sum(node => (long)node.Amount));
            var times = requiredCoins > 0 ? coins / requiredCoins : int.MaxValue;

            foreach (var (type, count) in requiredItems) {
                times = Math.Min(times, counts.GetValueOrDefault(type) / count);
            }

            if (rule.Length > 0 && times > bestTimes) {
                best = rule;
                bestTimes = times;
            }
        }

        var threshold = mode == 1 ? cap : 1;

        return bestTimes < threshold ? (null, 0) : (best, mode == 0 ? 1 : mode == 1 ? cap : (int)Math.Min(bestTimes, cap));
    }

    // Surplus offered furni stays in inventory; a voucher covering a coin node is deposited intact.
    public static uint[] Payment(WiredChestContract contract, InventoryItem[] offered, int mode, int cap)
    {
        var match = Match(contract, offered, mode, cap);

        if (match.Times == 0) {
            return [];
        }

        if (match.Rule == null) {
            return offered.Select(item => item.Id).ToArray();
        }

        var result = new List<uint>();
        var neededCoins = match.Rule.Where(node => node.ItemType == null).Sum(node => (long)node.Amount) * match.Times;

        foreach (var item in offered.Where(item => item.Definition.InteractionType == InteractionType.Exchange)) {
            if (neededCoins <= 0) {
                break;
            }

            result.Add(item.Id);
            neededCoins -= item.Definition.BehaviourData;
        }

        foreach (var group in match.Rule.Where(node => node.ItemType != null).GroupBy(node => node.ItemType!.Value)) {
            var needed = checked((int)(group.Sum(node => (long)node.Amount) * match.Times));
            result.AddRange(offered.Where(item => item.Definition.InteractionType != InteractionType.Exchange
                && WiredChestItemType.Of(item) == group.Key).Take(needed).Select(item => item.Id));
        }

        return result.ToArray();
    }

    public static WiredChestNode[] Scale(WiredChestNode[]? nodes, int times) =>
        (nodes ?? []).Select(node => node with { Amount = checked(node.Amount * times) }).ToArray();
}
