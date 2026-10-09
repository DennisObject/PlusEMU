using Plus.Communication.Packets.Incoming.Rooms;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Chests;
using Plus.HabboHotel.Rooms;

namespace Plus.Communication.Packets.Incoming.WiredChests;

// Parse the complete bounded payload before entering the room engine; rejected packets never write.
public abstract class WiredChestPacketEvent : RoomPacketEvent
{
    protected abstract Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet);
    public override Task Parse(Room room, GameClient session, IIncomingPacket packet)
    {
        try {
            var action = Read(room, session, packet);

            if (action != null && !packet.HasDataRemaining()) {
                room.GetWired().WithChests(action);
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or IOException or OverflowException) { }

        return Task.CompletedTask;
    }
    protected static Item? Chest(Room room, uint id) => room.GetRoomItemHandler().GetItem(id) is { IsTemporary: false } item
        && WiredChestFurniture.IsChest(item.Definition) ? item : null;
    protected static void Settings(WiredChestRoom module, Room room, GameClient session, Item item, Func<WiredChestSettings, WiredChestSettings> change)
    {
        var saved = module.Store.SaveSettings(item, session.GetHabbo().Id, room.GetWired().Settings.CanModify(session), room.OwnerId == session.GetHabbo().Id, change);
        session.Send(new WiredChestSettingsAckComposer(item.Id, saved));

        if (saved) {
            module.Refresh(item);
        }

        // This is authoritative state, also on refusal: client options changed optimistically.
        module.Open(session, item);
    }
    protected static void Withdraw(WiredChestRoom module, Room room, GameClient session, Item item, WiredChestItemType? type, int amount, bool allFurni = false)
    {
        var chest = module.Read(item);

        if (chest == null || !chest.CanWithdraw((uint)session.GetHabbo().Id, room.GetWired().Settings.CanModify(session))) {
            return;
        }

        var available = type == null && !allFurni ? chest.Coins : chest.Items.Count(furni => allFurni || WiredChestItemType.Of(furni) == type);
        var take = amount < 0 ? available : amount;

        if (take <= 0) {
            return;
        }

        var rewards = allFurni ? chest.Items.GroupBy(WiredChestItemType.Of).Select(group => new WiredChestNode(group.Count(), group.Key)).ToArray()
            : new[] { new WiredChestNode(take, type) };
        var result = module.Move(session, new()
        {
            RoomId = room.Id,
            UserId = session.GetHabbo().Id,
            SourceId = item.Id,
            ChestIds = [item.Id],
            Reward = rewards,
            CanModify = room.GetWired().Settings.CanModify(session)
        });

        if (result.Failure is { } failure) {
            session.SendNotification($"Chest transaction refused ({(int)failure}).");
        }

        module.Open(session, item);
    }
}

public sealed class ChestOpenEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());

        return item == null ? null : module => module.Open(session, item);
    }
}
public sealed class ChestCloseEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());

        return item == null ? null : module => module.Close(session, item);
    }
}
public sealed class ChestStartDepositEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());

        return item == null ? null : module => module.Start(session, item.Id, [item], null);
    }
}
public sealed class ChestDepositInventoryItemEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var inventoryId = packet.ReadUInt();

        return item == null ? null : module =>
        {
            var result = module.Move(session, new()
            {
                RoomId = room.Id,
                UserId = session.GetHabbo().Id,
                SourceId = item.Id,
                ChestIds = [item.Id],
                PaymentIds = [inventoryId],
                CanModify = room.GetWired().Settings.CanModify(session)
            });

            if (result.Failure is { } failure) {
                session.SendNotification($"Chest transaction refused ({(int)failure}).");
            }

            module.Open(session, item);
        };
    }
}
public sealed class ChestWithdrawAllEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());

        return item == null ? null : module => Withdraw(module, room, session, item, null, -1, true);
    }
}
public sealed class ChestWithdrawFurniEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var type = new WiredChestItemType(packet.ReadBool(), packet.ReadInt(), packet.ReadString());
        var amount = packet.ReadInt();

        return item == null ? null : module => Withdraw(module, room, session, item, type, amount);
    }
}
public sealed class ChestWithdrawCoinsEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var currency = packet.ReadInt();
        var amount = packet.ReadInt();

        return item == null || currency != -1 ? null : module => Withdraw(module, room, session, item, null, amount);
    }
}
public sealed class ChestDepositCoinsEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var currency = packet.ReadInt();
        var amount = packet.ReadInt();

        return item == null || currency != -1 || amount <= 0 ? null : module =>
        {
            var result = module.Move(session, new()
            {
                RoomId = room.Id,
                UserId = session.GetHabbo().Id,
                SourceId = item.Id,
                ChestIds = [item.Id],
                WalletDepositCredits = amount,
                CanModify = room.GetWired().Settings.CanModify(session)
            });

            if (result.Failure is { } failure) {
                session.SendNotification($"Chest transaction refused ({(int)failure}).");
            }

            module.Open(session, item);
        };
    }
}
public sealed class ChestSaveOptionsEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var locked = packet.ReadBool();
        var autoLock = packet.ReadBool();
        var capacity = packet.ReadInt();

        return item == null ? null : module => Settings(module, room, session, item, old => old with { Locked = locked, AutoLock = autoLock, Capacity = capacity });
    }
}
public sealed class ChestSavePreferencesEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var name = packet.ReadString();
        var description = packet.ReadString();
        var open = packet.ReadBool();
        var donate = packet.ReadBool();
        var state = packet.ReadInt();
        var preview = packet.ReadInt();
        var amount = packet.ReadInt();

        return item == null ? null : module => Settings(module, room, session, item, old => old with
        { Name = name, Description = description, EveryoneCanOpen = open, EveryoneCanDonate = donate, StateMode = state, PreviewMode = preview, PreviewAmount = amount });
    }
}
public sealed class ChestEnableWiredEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());

        return item == null ? null : module => Settings(module, room, session, item, old => old with { WiredEnabled = true });
    }
}
public sealed class ChestSaveNotificationsEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var full = packet.ReadBool();
        var donation = packet.ReadBool();
        var withdrawal = packet.ReadBool();
        var empty = packet.ReadBool();
        var wired = packet.ReadBool();
        var mode = packet.ReadInt();

        return item == null ? null : module => Settings(module, room, session, item, old => old with
        { NotifyFull = full, NotifyDonation = donation, NotifyWithdrawal = withdrawal, NotifyEmpty = empty, NotifyTransaction = wired, NotifyMode = mode });
    }
}
public sealed class WiredChestOfferItemsEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var remove = packet.ReadBool();
        var count = packet.ReadInt();

        if (count is < 0 or > 500) {
            return null;
        }

        var ids = new uint[count];

        for (var i = 0; i < count; i++) {
            ids[i] = packet.ReadUInt();
        }

        return module => module.Offer(session, ids, !remove);
    }
}
public sealed class WiredChestAcceptEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var final = packet.ReadBool();

        return module => module.Confirm(session, final);
    }
}
public sealed class WiredChestCancelEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet) => module => module.Cancel(session);
}

public sealed class ChestUpgradeEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var item = Chest(room, packet.ReadUInt());
        var count = packet.ReadInt();

        return item == null ? null : module => module.Upgrade(session, item, count);
    }
}

public sealed class WiredChestLockEvent : WiredChestPacketEvent
{
    protected override Action<WiredChestRoom>? Read(Room room, GameClient session, IIncomingPacket packet)
    {
        var locked = packet.ReadBool();
        var all = packet.ReadBool();

        return module => module.Lock(session, locked, all);
    }
}
