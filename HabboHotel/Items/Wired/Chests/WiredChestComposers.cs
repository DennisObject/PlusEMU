using Plus.Communication.Packets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items.Wired.Chests;

// Octane's 93xx chest envelope is deliberately separate from Sulake's native packet grammar.
public sealed class WiredChestContentsComposer(WiredChestSnapshot chest, bool owner = false, int spriteId = 0, bool starter = false, bool canWithdraw = false, bool canDeposit = false, bool canLock = false) : IServerPacket
{
    public uint MessageId => 9312;
    public void Compose(IOutgoingPacket packet)
    {
        var s = chest.Settings;
        packet.WriteUInt(chest.Id);
        packet.WriteString(s.Name);
        packet.WriteString(s.Description);
        packet.WriteInt(starter ? 100 : chest.MaximumCapacity);
        packet.WriteInt(chest.Amount);
        packet.WriteBool(s.EveryoneCanOpen);
        packet.WriteBool(s.EveryoneCanDonate);
        packet.WriteInt(s.StateMode);
        packet.WriteBool(s.NotifyFull);
        packet.WriteBool(s.NotifyDonation);
        packet.WriteBool(s.NotifyWithdrawal);
        packet.WriteBool(s.NotifyEmpty);
        packet.WriteBool(s.NotifyTransaction);
        packet.WriteInt(s.NotifyMode);
        packet.WriteInt(chest.Kind == WiredChestKind.Coins ? 1 : 0);

        if (chest.Kind == WiredChestKind.Coins) {
            packet.WriteInt(-1);
            packet.WriteInt(chest.Coins);
        }

        packet.WriteInt(chest.Kind == WiredChestKind.Coins ? 0 : 1);
        var groups = chest.Items.GroupBy(item => item.Definition.Id).ToArray();
        packet.WriteInt(groups.Length);

        foreach (var group in groups) {
            packet.WriteUInt(group.Key);
            packet.WriteInt(group.Count());
        }

        packet.WriteBool(s.Locked);
        packet.WriteInt(s.Capacity);
        packet.WriteBool(s.AutoLock);
        packet.WriteBool(owner);
        packet.WriteInt(spriteId);
        packet.WriteBool(s.WiredEnabled);
        packet.WriteBool(starter);
        packet.WriteInt(s.PreviewMode);
        packet.WriteInt(s.PreviewAmount);
        // Optional Octane tail: server-authorized controls; older parsers ignore it.
        packet.WriteBool(canWithdraw);
        packet.WriteBool(canDeposit);
        packet.WriteBool(canLock);
    }
}

public sealed class WiredChestTradeOpenComposer(WiredChestContract? contract, WiredChestKind kind, int mode, int multiplier, int timeout, bool replacement) : IServerPacket
{
    public uint MessageId => 9331;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInt(contract == null ? 3 : (int)contract.Kind);
        packet.WriteString(contract?.ReceiveText ?? "");
        packet.WriteString(contract?.Layout ?? "generic");
        packet.WriteBool(true);
        packet.WriteBool(replacement);
        packet.WriteInt(timeout);
        var rules = contract?.Payment ?? [];
        packet.WriteInt(rules.Length);

        foreach (var rule in rules) {
            WiredChestOctaneWire.Rule(packet, WiredChestContracts.Scale(rule, mode == 1 ? multiplier : 1));
        }

        packet.WriteBool(contract?.Reward != null);

        if (contract?.Reward != null) {
            WiredChestOctaneWire.Rule(packet, WiredChestContracts.Scale(contract.Reward, mode == 1 ? multiplier : 1));
        }
    }
}

public sealed class WiredChestTradeItemsComposer(int userId, InventoryItem[] offer, WiredChestContract? contract, int mode, int multiplier,
    bool accepted = false, int secondsLeft = 0) : IServerPacket
{
    public uint MessageId => 9332;
    public void Compose(IOutgoingPacket packet)
    {
        var times = contract == null ? offer.Length > 0 ? 1 : 0 : WiredChestContracts.Times(contract, offer, mode, multiplier);
        packet.WriteInt(accepted ? 3 : 1);
        packet.WriteBool(times > 0);
        packet.WriteInt(secondsLeft);
        packet.WriteInt(offer.Length);

        foreach (var item in offer) {
            packet.WriteUInt(item.Id);
            packet.WriteBool(item.IsWallItem);
            packet.WriteInt(item.Definition.SpriteId);
        }

        var rewards = WiredChestContracts.Scale(contract?.Reward, Math.Max(1, times));
        var furni = rewards.Where(node => node.ItemType != null).ToArray();
        packet.WriteInt(furni.Length);

        foreach (var node in furni) {
            packet.WriteInt(node.ItemType!.Value.SpriteId);
            packet.WriteInt(node.Amount);
        }

        var coins = rewards.Where(node => node.ItemType == null).Sum(node => node.Amount);
        packet.WriteInt(coins > 0 ? 1 : 0);

        if (coins > 0) {
            packet.WriteInt(-1);
            packet.WriteInt(coins);
        }

        var missing = times == 0 ? contract?.Payment?.FirstOrDefault() ?? [] : [];
        packet.WriteInt(missing.Length);

        foreach (var node in missing) {
            WiredChestOctaneWire.Node(packet, node);
        }
    }
}

public sealed class WiredChestTradeEndComposer(WiredChestFailure? failure) : IServerPacket
{
    public uint MessageId => failure == null ? 9334U : 9333U;
    public void Compose(IOutgoingPacket packet)
    {
        if (failure != null) {
            packet.WriteInt((int)failure.Value);
        }
    }
}

internal static class WiredChestOctaneWire
{
    public static void Rule(IOutgoingPacket packet, WiredChestNode[] nodes)
    {
        packet.WriteInt(nodes.Length);

        foreach (var node in nodes) {
            Node(packet, node);
        }
    }
    public static void Node(IOutgoingPacket packet, WiredChestNode node)
    {
        packet.WriteInt(node.ItemType == null ? 0 : 1);
        packet.WriteInt(node.ItemType == null ? -1 : 0);
        packet.WriteBool(node.ItemType?.Wall ?? false);
        packet.WriteInt(node.ItemType?.SpriteId ?? 0);
        packet.WriteInt(node.Amount);
    }
}

public sealed class WiredChestFurniChunkComposer(uint chestId, int pages, int page, InventoryItem[] items, IReadOnlyDictionary<uint, long>? depositTransactions = null) : IServerPacket
{
    public uint MessageId => 9322;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInt(chestId);
        packet.WriteInt(pages);
        packet.WriteInt(page);
        packet.WriteInt(items.Length);

        foreach (var item in items) {
            var type = WiredChestItemType.Of(item);
            packet.WriteUInt(item.Id);
            packet.WriteInt(0);
            var deposit = depositTransactions?.GetValueOrDefault(item.Id) ?? 0;
            packet.WriteInt(unchecked((int)(deposit >> 32)));
            packet.WriteInt(unchecked((int)deposit));
            packet.WriteBool(type.Wall);
            packet.WriteInt(type.SpriteId);
            packet.WriteString(type.PosterId);
            packet.WriteBool(item.Definition.AllowInventoryStack);
            packet.WriteInt((int)item.Definition.Category);
            Plus.Communication.Packets.Outgoing.FurnitureDataSerializer.Write(packet,
                FurnitureDataSnapshot.Capture(item.ExtraData), item.UniqueNumber, item.UniqueSeries);

            if (!type.Wall) {
                packet.WriteInt(0);
            }
        }
    }
}

public sealed class WiredChestRewardComposer(WiredChestTransferResult result, string text, bool open) : IServerPacket
{
    public uint MessageId => 9346;
    public void Compose(IOutgoingPacket packet)
    {
        var nodes = result.Received.GroupBy(WiredChestItemType.Of).Select(group => new WiredChestNode(group.Count(), group.Key)).ToList();

        if (result.Figures.WithdrawalCoins > 0) {
            nodes.Insert(0, new(result.Figures.WithdrawalCoins));
        }

        WiredChestOctaneWire.Rule(packet, nodes.ToArray());
        packet.WriteString(text);
        packet.WriteBool(open);
    }
}

public sealed class WiredChestUpgradeComposer(uint chestId, int result) : IServerPacket
{
    public uint MessageId => 9335;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInt(chestId);
        packet.WriteInt(result);
    }
}

public sealed class WiredChestSettingsAckComposer(uint chestId, bool saved) : IServerPacket
{
    public uint MessageId => 9347;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteUInt(chestId);
        packet.WriteBool(saved);
    }
}

public sealed class WiredChestLockComposer(bool locked, bool all, int affected) : IServerPacket
{
    public uint MessageId => 9329;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBool(locked);
        packet.WriteBool(all);
        packet.WriteInt(affected);
    }
}
