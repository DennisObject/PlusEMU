using Dapper;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Users;
using Plus.Database;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using static Plus.HabboHotel.Housekeeping.HousekeepingErrors;
using static Plus.HabboHotel.Housekeeping.HousekeepingUserTargets;

namespace Plus.HabboHotel.Housekeeping;

public enum HousekeepingCurrency
{
    Credits = -1,
    Duckets = 0,
    Diamonds = 5
}

public interface IHousekeepingEconomyActions
{
    HousekeepingOutcome Give(Habbo actor, int userId, HousekeepingCurrency currency, int amount);
    HousekeepingOutcome GrantItem(Habbo actor, int userId, int itemId, int quantity);
    HousekeepingOutcome SetClub(Habbo actor, int userId, int days);
}

public sealed class HousekeepingEconomyActions : IHousekeepingEconomyActions
{
    // These need linked rows or data a bare inventory item cannot carry.
    private static readonly HashSet<InteractionType> UngrantableItems = new()
    {
        InteractionType.Teleport, InteractionType.Moodlight, InteractionType.Toner, InteractionType.Gift, InteractionType.Trophy,
        InteractionType.GuildItem, InteractionType.GuildGate, InteractionType.GuildForum, InteractionType.BadgeDisplay,
        InteractionType.Badge, InteractionType.Pet, InteractionType.Bot, InteractionType.PurchasableClothing
    };

    private readonly IHousekeepingUserStore _users;
    private readonly IAccessControl _permissions;
    private readonly IGameClientManager _clients;
    private readonly IItemDataManager _itemData;
    private readonly IItemFactory _itemFactory;
    private readonly IClubMembershipService _clubMemberships;
    private readonly IDatabase _database;
    // Held for every write to an account so it cannot interleave with that account's login.
    private readonly IAccountSessionGate _sessionGate;

    public HousekeepingEconomyActions(IHousekeepingUserStore users, IGameClientManager clients, IItemDataManager itemData, IItemFactory itemFactory,
        IClubMembershipService clubMemberships, IDatabase database, IAccountSessionGate sessionGate, IAccessControl permissions)
    {
        _users = users;
        _permissions = permissions;
        _clients = clients;
        _itemData = itemData;
        _itemFactory = itemFactory;
        _clubMemberships = clubMemberships;
        _database = database;
        _sessionGate = sessionGate;
    }

    public HousekeepingOutcome Give(Habbo actor, int userId, HousekeepingCurrency currency, int amount)
    {
        if (!Enum.IsDefined(currency) || !HousekeepingLimits.InRange(amount, 1, HousekeepingLimits.MaxGrantAmount)) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) return denied;
        var detail = $"currency={currency} amount={amount}";
        if (_clients.Online(userId) is { } client)
        {
            var habbo = client.GetHabbo();
            // Logout and the currency timer save the wallet under this lock, so the grant cannot be lost to them.
            lock (habbo.WalletSync)
            {
                if (habbo.WalletClosed) return HousekeepingOutcome.Fail(EconomyFailed, Label(user), detail);
                var balance = HousekeepingLimits.AddToBalance(Balance(habbo, currency), amount);
                if (balance == null) return HousekeepingOutcome.Fail(BalanceOverflow, Label(user), detail);
                SetBalance(habbo, currency, balance.Value);
                client.Send(currency == HousekeepingCurrency.Credits
                    ? new CreditBalanceComposer(balance.Value)
                    : new HabboActivityPointNotificationComposer(balance.Value, amount, (int)currency));
            }
            return HousekeepingOutcome.Success(Label(user), detail);
        }
        // Offline wallets live in the users row. The session gate keeps logins from loading it mid-grant, and
        // logout saves the live wallet before the session is unregistered.
        int updated;
        using (var connection = _database.Connection())
            updated = connection.Execute($"UPDATE `users` SET `{Column(currency)}` = `{Column(currency)}` + @amount " +
                                         $"WHERE `id` = @userId AND `{Column(currency)}` <= @limit", new { amount, userId, limit = int.MaxValue - amount });
        return updated == 1 ? HousekeepingOutcome.Success(Label(user), detail) : HousekeepingOutcome.Fail(EconomyFailed, Label(user), detail);
    }

    public HousekeepingOutcome GrantItem(Habbo actor, int userId, int itemId, int quantity)
    {
        if (itemId <= 0 || !HousekeepingLimits.InRange(quantity, 1, HousekeepingLimits.MaxItemQuantity)) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) return denied;
        if (!_itemData.Items.TryGetValue((uint)itemId, out var definition) || UngrantableItems.Contains(definition.InteractionType))
            return HousekeepingOutcome.Fail(ItemNotFound, Label(user), $"itemId={itemId}");
        var items = _itemFactory.CreateMultipleItems(definition, user.Id, string.Empty, quantity);
        if (_clients.Online(userId) is { } client)
        {
            foreach (var item in items)
            {
                if (client.GetHabbo().Inventory.Furniture.AddItem(item.ToInventoryItem()))
                    client.Send(new FurniListNotificationComposer(item.Id, 1));
            }
            client.Send(new FurniListUpdateComposer());
        }
        return HousekeepingOutcome.Success(Label(user), $"itemId={itemId} item={definition.ItemName} quantity={quantity}");
    }

    public HousekeepingOutcome SetClub(Habbo actor, int userId, int days)
    {
        if (!HousekeepingLimits.InRange(days, 0, HousekeepingLimits.MaxClubDays)) return HousekeepingOutcome.Invalid(HousekeepingTarget.User(Math.Max(userId, 0)));
        using var account = _sessionGate.Enter(userId);
        if (_users.Target(actor, userId, _permissions, out var user) is { } denied) return denied;
        var expiry = _clubMemberships.Grant(actor, user.Id, days);
        if (expiry == null) return HousekeepingOutcome.Fail(Forbidden, Label(user));
        if (_clients.Online(userId) is { } client)
            client.Send(new ScrSendUserInfoComposer(ClubStatusSnapshot.Capture(client.GetHabbo().Access)));
        return HousekeepingOutcome.Success(Label(user), $"days={days} expires={expiry.Value.ToUnixTimeSeconds()}");
    }

    private static int Balance(Habbo habbo, HousekeepingCurrency currency) => currency switch
    {
        HousekeepingCurrency.Credits => habbo.Credits,
        HousekeepingCurrency.Duckets => habbo.Duckets,
        _ => habbo.Diamonds
    };

    private static void SetBalance(Habbo habbo, HousekeepingCurrency currency, int balance)
    {
        switch (currency)
        {
            case HousekeepingCurrency.Credits: habbo.Credits = balance; break;
            case HousekeepingCurrency.Duckets: habbo.Duckets = balance; break;
            default: habbo.Diamonds = balance; break;
        }
    }

    private static string Column(HousekeepingCurrency currency) => currency switch
    {
        HousekeepingCurrency.Credits => "credits",
        HousekeepingCurrency.Duckets => "activity_points",
        _ => "vip_points"
    };
}
