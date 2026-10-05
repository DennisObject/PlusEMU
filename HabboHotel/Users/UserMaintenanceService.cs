using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Users;

public enum UserCurrency { Credits, Duckets, Diamonds, Gotw }

/// <summary>Staff currency and motto maintenance: every balance change is persisted before memory or the client sees it.</summary>
public interface IUserMaintenanceService
{
    Task<bool> GiveCurrency(int userId, string currency, int amount);
    Task<bool> TakeCurrency(int userId, string currency, int amount);
    Task<bool> SyncCurrency(int userId, string currency);
    Task<bool> ReloadCurrency(int userId, string currency);
    Task<bool> ReloadMotto(int userId);
}

public sealed class UserMaintenanceService(IUserMaintenanceStore store, IAccountSessionGate sessionGate, IGameClientManager clients) : IUserMaintenanceService
{
    public Task<bool> GiveCurrency(int userId, string currency, int amount) => ChangeCurrency(userId, currency, amount, 1);

    public Task<bool> TakeCurrency(int userId, string currency, int amount) => ChangeCurrency(userId, currency, amount, -1);

    public async Task<bool> SyncCurrency(int userId, string currency)
    {
        if (!TryCurrency(currency, out var type)) return false;
        using var held = await sessionGate.EnterAsync(userId);
        var client = clients.GetClientByUserId(userId);
        var habbo = client?.GetHabbo();
        if (client == null || habbo == null) return false;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return false;
            return store.TryWriteCurrency(userId, type, Balance(habbo, type));
        }
    }

    public async Task<bool> ReloadCurrency(int userId, string currency)
    {
        if (!TryCurrency(currency, out var type)) return false;
        using var held = await sessionGate.EnterAsync(userId);
        var client = clients.GetClientByUserId(userId);
        var habbo = client?.GetHabbo();
        if (client == null || habbo == null) return false;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return false;
            if (store.ReadCurrency(userId, type) is not { } value) return false;
            SetBalance(habbo, type, value);
            Publish(client, habbo, type, value);
            return true;
        }
    }

    public async Task<bool> ReloadMotto(int userId)
    {
        using var held = await sessionGate.EnterAsync(userId);
        var client = clients.GetClientByUserId(userId);
        var habbo = client?.GetHabbo();
        if (client == null || habbo == null) return false;
        if (store.ReadMotto(userId) is not { } motto) return false;
        habbo.Motto = motto;

        // Outside a room there is nothing to publish; the stored motto is already the current one.
        if (!habbo.InRoom) return true;
        var room = habbo.CurrentRoom;
        var user = room?.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id);
        if (room == null || user == null) return false;
        room.SendPacket(new UserChangeComposer(AvatarChangeSnapshot.Capture(user, false)));
        return true;
    }

    private async Task<bool> ChangeCurrency(int userId, string currency, int amount, int sign)
    {
        if (!TryCurrency(currency, out var type)) return false;
        using var held = await sessionGate.EnterAsync(userId);
        var client = clients.GetClientByUserId(userId);
        var habbo = client?.GetHabbo();
        if (client == null || habbo == null) return false;
        lock (habbo.WalletSync)
        {
            if (habbo.WalletClosed) return false;
            var next = (long)Balance(habbo, type) + (long)sign * amount;
            if (next is < int.MinValue or > int.MaxValue) return false;
            if (!store.TryWriteCurrency(userId, type, (int)next)) return false;
            SetBalance(habbo, type, (int)next);
            Publish(client, habbo, type, amount);
            return true;
        }
    }

    // Aliases are the RCON contract: coins and credits are one balance, pixels and duckets are one balance.
    private static bool TryCurrency(string? alias, out UserCurrency currency)
    {
        switch (alias)
        {
            case "coins" or "credits": currency = UserCurrency.Credits; return true;
            case "pixels" or "duckets": currency = UserCurrency.Duckets; return true;
            case "diamonds": currency = UserCurrency.Diamonds; return true;
            case "gotw": currency = UserCurrency.Gotw; return true;
            default: currency = default; return false;
        }
    }

    private static int Balance(Habbo habbo, UserCurrency currency) => currency switch
    {
        UserCurrency.Credits => habbo.Credits,
        UserCurrency.Duckets => habbo.Duckets,
        UserCurrency.Diamonds => habbo.Diamonds,
        _ => habbo.GotwPoints,
    };

    private static void SetBalance(Habbo habbo, UserCurrency currency, int value)
    {
        switch (currency)
        {
            case UserCurrency.Credits: habbo.Credits = value; break;
            case UserCurrency.Duckets: habbo.Duckets = value; break;
            case UserCurrency.Diamonds: habbo.Diamonds = value; break;
            default: habbo.GotwPoints = value; break;
        }
    }

    // The duckets notification carries the signed change for give and take, and the loaded value for reload; take keeps its legacy positive amount.
    private static void Publish(GameClient client, Habbo habbo, UserCurrency currency, int pixelAmount)
    {
        switch (currency)
        {
            case UserCurrency.Credits: client.Send(new CreditBalanceComposer(habbo.Credits)); break;
            case UserCurrency.Duckets: client.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, pixelAmount)); break;
            case UserCurrency.Diamonds: client.Send(new HabboActivityPointNotificationComposer(habbo.Diamonds, 0, 5)); break;
            default: client.Send(new HabboActivityPointNotificationComposer(habbo.GotwPoints, 0, 103)); break;
        }
    }
}
