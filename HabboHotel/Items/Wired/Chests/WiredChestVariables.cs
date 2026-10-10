using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;

namespace Plus.HabboHotel.Items.Wired.Chests;

public static class WiredChestVariables
{
    public static bool Supports(WiredVariableTarget target, string name) => target switch
    {
        WiredVariableTarget.Furni => name is "~chest.available_amount" or "~chest.capacity" or "~chest.is_auto_lock"
            or "~chest.is_open" or "~chest.is_donatable" or "~chest.locked",
        WiredVariableTarget.User => name is "@transaction.in_trade" or "@transaction.start_time" or "@transaction.contract_id"
            or "@transaction.state" or "@transaction.can_accept" or "@transaction.current_multiplier",
        WiredVariableTarget.Context => name is "@event.transaction_complete.multiplier" or "@event.transaction_complete.deposit.furni_count"
            or "@event.transaction_complete.deposit.coins_count" or "@event.transaction_complete.withdrawal.furni_count"
            or "@event.transaction_complete.withdrawal.coins_count" or "@event.transaction_failed.reason",
        _ => false
    };
    public static bool HasNumericValue(WiredVariableTarget target, string name) => Supports(target, name)
        && name is not ("~chest.is_auto_lock" or "~chest.is_open" or "~chest.is_donatable" or "~chest.locked"
            or "@transaction.in_trade" or "@transaction.can_accept");
    public static long? Read(WiredChestRoom chests, WiredRuntimeContext? context, WiredVariableHolder holder, string name, Room? liveRoom = null)
    {
        var room = context?.Room ?? liveRoom;

        if (room == null || !Supports(holder.Target, name)) {
            return null;
        }

        if (holder.Target == WiredVariableTarget.User) {
            var actor = room.GetRoomUserManager().GetRoomUserByVirtualId(holder.EntityId);

            return actor == null || actor.IsBot || WiredVariableRuntimeFrames.UserHolder(actor) != holder
                || context != null && (!context.UserIdentity.TryGetValue(actor.VirtualId, out var original) || !ReferenceEquals(actor, original))
                ? null : chests.ReadTransaction(actor, name);
        }

        if (holder.Target == WiredVariableTarget.Furni) {
            var item = room.GetRoomItemHandler().GetItem(unchecked((uint)holder.EntityId));

            if (item == null || WiredVariableRuntimeFrames.FurniHolder(item) != holder
                || context != null && (!context.FurniIdentity.TryGetValue(item.Id, out var original) || !ReferenceEquals(item, original))
                || chests.Read(item) is not { } chest) {
                return null;
            }

            return name switch
            {
                "~chest.available_amount" => chest.Amount,
                "~chest.capacity" => chest.Settings.Capacity,
                "~chest.is_auto_lock" => chest.Settings.AutoLock ? 1 : null,
                "~chest.is_open" => chest.Settings.EveryoneCanOpen ? 1 : null,
                "~chest.is_donatable" => chest.Settings.EveryoneCanDonate ? 1 : null,
                "~chest.locked" => chest.Settings.Locked ? 1 : null,
                _ => null
            };
        }

        if (context == null) {
            return null;
        }

        if (name == "@event.transaction_failed.reason") {
            return context.Event.Kind == WiredEventKind.TransactionFail ? context.Event.Code : null;
        }

        if (context.Event.Kind != WiredEventKind.TransactionComplete || context.Event.Transaction is not { } figures) {
            return null;
        }

        return name switch
        {
            "@event.transaction_complete.multiplier" => figures.Multiplier,
            "@event.transaction_complete.deposit.furni_count" => figures.DepositFurni,
            "@event.transaction_complete.deposit.coins_count" => figures.DepositCoins,
            "@event.transaction_complete.withdrawal.furni_count" => figures.WithdrawalFurni,
            "@event.transaction_complete.withdrawal.coins_count" => figures.WithdrawalCoins,
            _ => null
        };
    }
}
