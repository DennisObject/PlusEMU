using System.Collections.Immutable;
using System.Text.Json;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;

namespace Plus.HabboHotel.Items.Wired.Chests;

// Physical contract editors retain Octane's alternatives grammar. It is not the dynamic addon grammar.
public static class WiredChestContractEditor
{
    public static void Open(WiredChestRoom module, GameClient client, Item item)
    {
        if (item.GetRoom() is not { } room || !room.GetWired().Settings.CanInspect(client) || module.Store.LoadContract(item) is not { } contract) {
            return;
        }

        var fields = new List<int> { -1, contract.Payment?.Length ?? 0 };
        var posters = new List<string>();
        var index = 0;
        void Node(WiredChestNode node)
        {
            fields.AddRange([node.ItemType == null ? 0 : 1, node.ItemType == null ? -1 : 0, node.ItemType?.Wall == true ? 1 : 0, node.ItemType?.SpriteId ?? 0, node.Amount]);

            if (node.ItemType is { PosterId.Length: > 0 } type) {
                posters.Add($"{index}={type.PosterId}");
            }

            index++;
        }

        foreach (var rule in contract.Payment ?? []) {
            fields.Add(rule.Length);

            foreach (var node in rule) {
                Node(node);
            }
        }

        fields.Add(contract.Reward?.Length ?? 0);

        foreach (var node in contract.Reward ?? []) {
            Node(node);
        }

        var descriptor = new WiredBoxDescriptor(item.Definition.ItemName, WiredBoxCategory.Action,
            contract.Kind == WiredContractKind.Payment ? 110 : contract.Kind == WiredContractKind.Reward ? 111 : 112,
            "Turbo f702041c/WiredTrading")
        { Support = WiredBoxSupport.Implemented };
        client.Send(new WiredEffectConfigComposer(new(item.Id, item.Definition.SpriteId, descriptor,
            new() { IntParams = fields.ToImmutableArray(), Text = "@contract:" + JsonSerializer.Serialize(new ContractFields { Posters = string.Join(',', posters), PaymentMode = contract.PaymentMode, ReceiveText = contract.ReceiveText, Layout = contract.Layout, RewardCategory = contract.RewardCategory, ShowDialog = contract.ShowDialog, RewardText = contract.RewardText }) }, 0, [])));
    }

    public static bool TrySave(WiredChestRoom module, Item item, WiredConfiguration configuration)
    {
        var data = configuration.IntParams;

        if (data.Length < 3 || data[0] != -1 || data[1] is < 0 or > 3 || configuration.SelectedItems.Length != 0) {
            return false;
        }

        ContractFields metadata;

        try {
            metadata = configuration.Text.StartsWith("@contract:", StringComparison.Ordinal)
                ? JsonSerializer.Deserialize<ContractFields>(configuration.Text[10..]) ?? throw new JsonException()
                : new() { Posters = configuration.Text };
        }
        catch (JsonException) {
            return false;
        }

        if (metadata.Posters == null || metadata.ReceiveText == null || metadata.RewardText == null) {
            return false;
        }

        var posters = new Dictionary<int, string>();

        foreach (var part in metadata.Posters.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            var split = part.IndexOf('=');

            if (split < 1 || !int.TryParse(part[..split], out var slot) || slot < 0 || part[(split + 1)..].Length > 20) {
                return false;
            }

            posters[slot] = part[(split + 1)..];
        }

        var cursor = 2;
        var index = 0;
        WiredChestNode[] Rule()
        {
            if (cursor >= data.Length || data[cursor] is < 0 or > 5) {
                throw new InvalidDataException("Invalid contract node count.");
            }

            var result = new WiredChestNode[data[cursor++]];

            for (var i = 0; i < result.Length; i++) {
                if (cursor + 5 > data.Length || data[cursor] is not (0 or 1) || data[cursor + 2] is not (0 or 1)
                    || data[cursor] == 0 && data[cursor + 1] != -1) {
                    throw new InvalidDataException("Invalid contract node.");
                }

                var type = data[cursor] == 1 ? new WiredChestItemType(data[cursor + 2] == 1, data[cursor + 3], posters.GetValueOrDefault(index, "")) : (WiredChestItemType?)null;
                result[i] = new(data[cursor + 4], type);
                index++;
                cursor += 5;
            }

            return result;
        }

        try {
            var give = new WiredChestNode[data[1]][];

            for (var i = 0; i < give.Length; i++) {
                give[i] = Rule();
            }

            var get = Rule();
            var kind = WiredChestFurniture.ContractKind(item.Definition);

            if (cursor != data.Length || kind == WiredContractKind.Payment && get.Length != 0 || kind == WiredContractKind.Reward && give.Any(rule => rule.Length != 0)) {
                return false;
            }

            return module.Store.SaveContract(item, new()
            {
                Id = item.Id,
                Kind = kind,
                Payment = kind == WiredContractKind.Reward ? [] : give,
                Reward = kind == WiredContractKind.Payment ? null : get,
                PaymentMode = metadata.PaymentMode,
                ReceiveText = metadata.ReceiveText,
                Layout = metadata.Layout,
                RewardCategory = metadata.RewardCategory,
                ShowDialog = metadata.ShowDialog,
                RewardText = metadata.RewardText
            });
        }
        catch (InvalidDataException) {
            return false;
        }
    }
    public sealed record ContractFields
    {
        public string Posters { get; init; } = "";
        public int PaymentMode { get; init; } = 1;
        public string ReceiveText { get; init; } = "";
        public string Layout { get; init; } = "generic";
        public int RewardCategory { get; init; } = 11;
        public bool ShowDialog { get; init; }
        public string RewardText { get; init; } = "";
    }
}
