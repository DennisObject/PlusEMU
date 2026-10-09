using System.Text.Json;
using System.Text.Json.Serialization;
using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory.Badges;
using Plus.HabboHotel.Users.Messenger.FriendBar;
using Bundle = Plus.HabboHotel.Users.Grants.GrantBundle;

namespace Plus.HabboHotel.Users.Grants;

/// <summary>
/// CMS grants. Every grant holds the account session gate, so a login either loads the account before the grant or waits
/// and loads it afterwards. Bundles and badge removals only touch offline accounts: a live session would overwrite them.
/// </summary>
public interface IUserGrantService
{
    Task<GrantOutcome> GrantBundle(int userId, string? key, string? payloadBase64);
    Task<GrantOutcome> GiveBadge(int userId, string? code);
    Task<GrantOutcome> TakeBadge(int userId, string? code);

    /// <summary>Sets the settings the logout save writes from memory (home room, friend bar state), online or offline.</summary>
    Task<GrantOutcome> UpdateSettings(int userId, string? payloadBase64);
}

public sealed class UserGrantService(IUserGrantStore store, IAccountSessionGate sessionGate, IGameClientManager clients, IItemDataManager itemData,
    IAccessControl access) : IUserGrantService
{
    public async Task<GrantOutcome> GrantBundle(int userId, string? key, string? payloadBase64)
    {
        if (userId <= 0 || !Bundle.ValidKey(key)) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "userId must be positive and the key 1..64 characters of [A-Za-z0-9._-].");
        }

        if (Bundle.TryParse(payloadBase64, out var bundle) is { } invalid) {
            return invalid;
        }

        // A retry of an applied grant replays its receipt even if the user has logged in since.
        if (store.FindReceipt(key!) is { } receipt) {
            return Replay(receipt, userId, bundle);
        }

        using var held = await sessionGate.EnterAsync(userId);

        // An earlier request with this key may have committed while this one waited, and the user may have logged in since.
        if (store.FindReceipt(key!) is { } committed) {
            return Replay(committed, userId, bundle);
        }

        if (Online(userId) != null) {
            return GrantOutcome.Fail(GrantOutcome.UserOnline);
        }

        // Definitions can change between retries, so they are checked only after a receipt had its chance to replay.
        foreach (var (baseId, _) in bundle.Furniture) {
            if (!itemData.Items.TryGetValue(baseId, out var definition)) {
                return GrantOutcome.Fail(GrantOutcome.UnknownFurniture, baseId.ToString());
            }

            if (!definition.AllowGift || definition.ProductType is not ("s" or "i") || ItemGrants.NeedsLinkedData(definition)) {
                return GrantOutcome.Fail(GrantOutcome.FurnitureNotGrantable, baseId.ToString());
            }
        }

        // Restricted badges are checked against the account's access before this bundle's rank change.
        return store.ApplyBundle(userId, key!, bundle, right => access.Can(userId, right))
            ?? (store.FindReceipt(key!) is { } claimed ? Replay(claimed, userId, bundle) : GrantOutcome.Fail(GrantOutcome.InternalError));
    }

    public async Task<GrantOutcome> GiveBadge(int userId, string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > Bundle.MaxBadgeCodeLength) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload);
        }

        using var held = await sessionGate.EnterAsync(userId);
        var habbo = Online(userId);

        if (habbo == null && !store.UserExists(userId)) {
            return GrantOutcome.Fail(GrantOutcome.UserNotFound);
        }

        // Read from the database, so a definition a CMS just inserted works without reloading the badge manager.
        if (store.FindBadge(code) is not { } definition) {
            return GrantOutcome.Fail(GrantOutcome.UnknownBadge, code);
        }

        if (definition.RequiredRight.Length > 0 && !(habbo?.Access.Can(definition.RequiredRight) ?? access.Can(userId, definition.RequiredRight))) {
            return GrantOutcome.Fail(GrantOutcome.RestrictedBadge, code);
        }

        var granted = store.InsertBadge(userId, definition.Code);

        if (habbo != null && !habbo.Inventory.Badges.HasBadge(definition.Code)) {
            habbo.Inventory.Badges.AddBadge(new Badge(definition.Code, 0));
            habbo.Client?.Send(new BadgesComposer(BadgeInventorySnapshot.Capture(habbo.Inventory.Badges.Badges.Values)));
            habbo.Client?.Send(new FurniListNotificationComposer(1, 4));
            habbo.Client?.Send(new BroadcastMessageAlertComposer("You have been given a new badge!"));
        }

        return GrantOutcome.Success(new { userId, badge = definition.Code, granted, online = habbo != null });
    }

    public async Task<GrantOutcome> TakeBadge(int userId, string? code)
    {
        if (string.IsNullOrEmpty(code) || code.Length > Bundle.MaxBadgeCodeLength) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload);
        }

        using var held = await sessionGate.EnterAsync(userId);

        if (Online(userId) != null) {
            return GrantOutcome.Fail(GrantOutcome.UserOnline);
        }

        if (!store.UserExists(userId)) {
            return GrantOutcome.Fail(GrantOutcome.UserNotFound);
        }

        return GrantOutcome.Success(new { userId, badge = code, removed = store.DeleteBadge(userId, code) });
    }

    public async Task<GrantOutcome> UpdateSettings(int userId, string? payloadBase64)
    {
        if (ParseSettings(payloadBase64) is not { } settings) {
            return GrantOutcome.Fail(GrantOutcome.InvalidPayload, "Expected Base64 JSON with homeRoom (>= 0) and/or friendBarState (0 or 1).");
        }

        using var held = await sessionGate.EnterAsync(userId);

        if (clients.GetClientByUserId(userId) is { } client && client.GetHabbo() is { } habbo) {
            // The logout save writes these from memory under WalletSync, so memory and row change together under it.
            lock (habbo.WalletSync) {
                if (!habbo.WalletClosed) {
                    var (error, homeRoom, friendBarState) = store.UpdateSettings(userId, settings.HomeRoom, settings.FriendBarState);

                    if (error != null) {
                        return GrantOutcome.Fail(error);
                    }

                    habbo.HomeRoom = homeRoom;
                    habbo.FriendbarState = FriendBarStateUtility.GetEnum(friendBarState);

                    if (settings.HomeRoom != null) {
                        client.Send(new NavigatorSettingsComposer(homeRoom));
                    }

                    return GrantOutcome.Success(new { userId, homeRoom, friendBarState, online = true });
                }
            }
        }

        var (failure, storedRoom, storedState) = store.UpdateSettings(userId, settings.HomeRoom, settings.FriendBarState);

        return failure != null ? GrantOutcome.Fail(failure) : GrantOutcome.Success(new { userId, homeRoom = storedRoom, friendBarState = storedState, online = false });
    }

    /// <summary>
    /// The live session of an account, or null once its logout has saved it. Logout saves and unregisters under WalletSync,
    /// so a closed wallet seen under that lock means nothing will write the account from memory again.
    /// </summary>
    internal static Habbo? Online(IGameClientManager clients, int userId)
    {
        if (clients.GetClientByUserId(userId)?.GetHabbo() is not { } habbo) {
            return null;
        }

        lock (habbo.WalletSync) {
            return habbo.WalletClosed ? null : habbo;
        }
    }

    private Habbo? Online(int userId) => Online(clients, userId);

    private static SettingsPayload? ParseSettings(string? base64)
    {
        if (string.IsNullOrEmpty(base64) || base64.Length > 1024) {
            return null;
        }

        try {
            var settings = JsonSerializer.Deserialize<SettingsPayload>(Convert.FromBase64String(base64), SettingsJson);

            return settings is { HomeRoom: not null } or { FriendBarState: not null } && settings.FriendBarState is null or 0 or 1 ? settings : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) {
            return null;
        }
    }

    private static GrantOutcome Replay(GrantReceipt receipt, int userId, Bundle bundle) =>
        receipt.UserId == userId && receipt.PayloadSha256 == bundle.Sha256
            ? new GrantOutcome(GrantOutcome.AlreadyApplied, JsonDocument.Parse(receipt.ResultJson).RootElement.Clone())
            : GrantOutcome.Fail(GrantOutcome.KeyPayloadMismatch, "The key was used for a different user or payload.");

    private static readonly JsonSerializerOptions SettingsJson = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
    };

    private sealed class SettingsPayload
    {
        [JsonPropertyName("homeRoom")]
        public uint? HomeRoom { get; init; }
        [JsonPropertyName("friendBarState")]
        public int? FriendBarState { get; init; }
    }
}
