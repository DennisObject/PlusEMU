using Plus.HabboHotel.Users.Inventory.Badges;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.HabboHotel.Users.Inventory.Furniture;

namespace Plus.HabboHotel.Items.Wired.Modern.Actions;

public sealed record WiredRewardEntry(int Type, string Code, int Probability);
public sealed class WiredRewardClaim
{
    private DateTimeOffset? _lastClaimAt;
    public long Count { get; set; }
    public DateTimeOffset? LastClaimAt
    {
        get => _lastClaimAt;
        set => _lastClaimAt = value?.ToUniversalTime();
    }
    public HashSet<string> ReceivedCodes { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class WiredRewardClaimJson
{
    public long Count { get; set; }
    public DateTimeOffset? LastClaimAt { get; set; }
    public HashSet<string> ReceivedCodes { get; set; } = new(StringComparer.Ordinal);
}
public static class WiredRewardClaimsJson
{
    public static Dictionary<int, WiredRewardClaim> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing reward claims.");
        var claims = new Dictionary<int, WiredRewardClaim>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
                || property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid reward claim entry.");
            var value = property.Value;
            var claim = new WiredRewardClaim
            {
                Count = ReadCount(value),
                LastClaimAt = ReadLastClaim(value),
                ReceivedCodes = ReadCodes(value)
            };
            claims[userId] = claim;
        }
        return claims;
    }

    public static string Serialize(IReadOnlyDictionary<int, WiredRewardClaim> claims) => JsonSerializer.Serialize(
        claims.ToDictionary(pair => pair.Key, pair => new WiredRewardClaimJson
        {
            Count = pair.Value.Count,
            LastClaimAt = pair.Value.LastClaimAt?.ToUniversalTime(),
            ReceivedCodes = new(pair.Value.ReceivedCodes, StringComparer.Ordinal)
        }));

    private static DateTimeOffset? ReadLastClaim(JsonElement value)
    {
        if (value.TryGetProperty(nameof(WiredRewardClaim.LastClaimAt), out var canonical))
        {
            if (canonical.ValueKind == JsonValueKind.Null) return null;
            if (canonical.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(canonical.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var instant))
                throw new InvalidDataException("Invalid canonical reward claim time.");
            return instant.ToUniversalTime();
        }
        if (!value.TryGetProperty("LastClaimUnix", out var legacy)) return null;
        if (legacy.ValueKind != JsonValueKind.Number || !legacy.TryGetDecimal(out var seconds))
            throw new InvalidDataException("Invalid legacy reward claim time.");
        if (seconds <= 0) return null;
        try
        {
            var ticks = decimal.ToInt64(decimal.Round(seconds * TimeSpan.TicksPerSecond,
                0, MidpointRounding.AwayFromZero));
            return DateTimeOffset.UnixEpoch.AddTicks(ticks);
        }
        catch (Exception error) when (error is OverflowException or ArgumentOutOfRangeException) { return null; }
    }

    private static long ReadCount(JsonElement value)
    {
        if (!value.TryGetProperty(nameof(WiredRewardClaim.Count), out var count)) return 0;
        if (count.ValueKind != JsonValueKind.Number || !count.TryGetInt64(out var parsedCount))
            throw new InvalidDataException("Invalid reward claim count.");
        return parsedCount;
    }

    private static HashSet<string> ReadCodes(JsonElement value)
    {
        if (!value.TryGetProperty(nameof(WiredRewardClaim.ReceivedCodes), out var codes))
            return new(StringComparer.Ordinal);
        if (codes.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid received reward codes.");
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var code in codes.EnumerateArray())
        {
            if (code.ValueKind != JsonValueKind.String || code.GetString() is not { } text)
                throw new InvalidDataException("Invalid received reward code.");
            result.Add(text);
        }
        return result;
    }
}
public sealed record WiredRewardGrant(int Reason, string? Badge = null, InventoryItem? Furniture = null);
public sealed record WiredRewardResultComposer(int Reason) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.WiredRewardResultComposer;
    public void Compose(IOutgoingPacket packet) => packet.WriteInteger(Reason);
}

public static class WiredRewards
{
    public static WiredConfiguration Defaults() => new() { IntParams = [0, 0, 0, 1, 0] };
    public static bool TryValidate(WiredConfiguration proposed, out WiredConfiguration validated, out string error)
    {
        validated = proposed; error = "Invalid reward configuration; only badge and furniture prizes are supported.";
        var p = proposed.IntParams;
        if (!WiredLegacyProtocol.IsWithinLimits(proposed) || p.Length != 5 || p[0] is < 0 or > 3
            || p[1] is < 0 or > 1 || p[2] is < 0 or > 1000 || p[3] is < 1 or > 1000
            || p[4] is not (0 or 10 or 11 or 200 or 201) || !TryEntries(proposed.Text, out _)) return false;
        validated = proposed with { UserSources = proposed.UserSources.SetItem("users", p[4]) }; error = ""; return true;
    }
    public static bool TryEntries(string text, out IReadOnlyList<WiredRewardEntry> entries)
    {
        var result = new List<WiredRewardEntry>(); entries = result;
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',');
            if (result.Count == 20 || parts.Length != 3 || !int.TryParse(parts[0], out var type) || type is not (0 or 1)
                || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var probability)) return false;
            var code = parts[1].Trim();
            if (code.Length is < 1 or > 100 || type == 0 && code.Length > 35) return false;
            if (type == 1 && code.Contains('#') && (!code.StartsWith("furni#", StringComparison.Ordinal)
                || !uint.TryParse(code[6..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)) return false;
            result.Add(new(type, code, Math.Clamp(probability, 0, 100)));
        }
        return true; // Empty defaults are editable but cannot award anything.
    }
    public static bool IntervalOpen(WiredRewardClaim claim, int interval, int count, DateTimeOffset now)
    {
        if (claim.Count == 0) return true;
        if (interval is < 1 or > 3 || count is < 1 or > 1000) return false;
        if (claim.LastClaimAt == null) return true;
        var unit = interval switch
        {
            1 => TimeSpan.TicksPerDay,
            2 => TimeSpan.TicksPerHour,
            _ => TimeSpan.TicksPerMinute
        };
        return now.ToUniversalTime() - claim.LastClaimAt >= TimeSpan.FromTicks(checked(unit * count));
    }
    public static WiredRewardEntry? Pick(IReadOnlyList<WiredRewardEntry> entries, bool unique, WiredRewardClaim claim, int roll)
    {
        var candidates = unique ? entries.Where(entry => !claim.ReceivedCodes.Contains(entry.Code)).ToArray() : entries.ToArray();
        if (unique) return candidates.Length == 0 ? null : candidates[(roll - 1) % candidates.Length];
        var cumulative = 0;
        foreach (var entry in candidates) { cumulative += entry.Probability; if (roll <= cumulative) return entry; }
        return null;
    }
    public static void Publish(Habbo habbo, WiredRewardGrant grant)
    {
        if (grant.Badge is { } badge)
        {
            habbo.Inventory.Badges.AddBadge(new(badge, 0));
            habbo.Client.Send(new BadgesComposer(BadgeInventorySnapshot.Capture(habbo.Inventory.Badges.Badges.Values)));
            habbo.Client.Send(new FurniListNotificationComposer(1, 4));
        }
        if (grant.Furniture is { } item)
        {
            habbo.Inventory.Furniture.AddItem(item);
            habbo.Client.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(item)));
            habbo.Client.Send(new FurniListUpdateComposer());
            habbo.Client.Send(new FurniListNotificationComposer(item.Id, item.IsFloorItem ? 1 : 2));
        }
        habbo.Client.Send(new WiredRewardResultComposer(grant.Reason));
    }
}

public interface IWiredRewardService
{
    bool Execute(Item box, WiredRuntimeContext context, WiredConfiguration config);
}

public sealed class WiredRewardService(IWiredRewardStore store, IItemDataManager definitions, TimeProvider clock,
    ILogger<WiredRewardService> logger) : IWiredRewardService
{
    public bool Execute(Item box, WiredRuntimeContext context, WiredConfiguration config)
    {
        if (box.IsTemporary || !WiredRewards.TryEntries(config.Text, out var prizes) || prizes.Count == 0) return false;
        var changed = false;
        foreach (var user in context.Targets.ResolveUsers(context, [], config.UserSources["users"]).Where(user => !user.IsBot))
        {
            var habbo = user.GetClient()?.GetHabbo();
            if (habbo == null || !ReferenceEquals(habbo.CurrentRoom, context.Room)) continue;
            WiredRewardGrant grant;
            try { grant = store.ClaimAndGrant(box, context.Room.Id, habbo, config, definitions, clock.GetUtcNow().ToUniversalTime()); }
            catch (Exception exception) { logger.LogError(exception, "Atomic wired reward failed for box {BoxId}.", box.Id); continue; }
            WiredRewards.Publish(habbo, grant);
            changed |= grant.Reason is 4 or 5;
        }
        return changed;
    }
}

public interface IWiredRewardStore
{
    WiredRewardGrant ClaimAndGrant(Item box, uint roomId, Habbo habbo, WiredConfiguration configuration,
        IItemDataManager definitions, DateTimeOffset now);
}

/// <summary>Durability boundary: quota and grant commit together. This module emits no packets or memory inventory writes.</summary>
public sealed class WiredRewardStore(IDatabase database) : IWiredRewardStore
{
    public WiredRewardGrant ClaimAndGrant(Item box, uint roomId, Habbo habbo, WiredConfiguration configuration,
        IItemDataManager definitions, DateTimeOffset now)
    {
        if (habbo.Id <= 0 || box.IsTemporary || !WiredRewards.TryValidate(configuration, out configuration, out _)
            || !WiredRewards.TryEntries(configuration.Text, out var prizes) || prizes.Count == 0) return new(0);
        now = now.ToUniversalTime();
        using var connection = database.Connection();
        if (connection.State != ConnectionState.Open) connection.Open();
        if (connection.ExecuteScalar<int>("SELECT COUNT(*) FROM information_schema.tables WHERE table_schema=DATABASE() AND table_name IN ('items','user_badges','wired_reward_state') AND ENGINE='InnoDB'") != 3)
            throw new InvalidOperationException("Atomic wired rewards require InnoDB for items, user_badges and wired_reward_state.");
        using var transaction = connection.BeginTransaction();
        // Validate authoritative placement and numeric room owner, including pickup while a grant was queued.
        var placed = connection.QuerySingleOrDefault<Placement>("SELECT i.room_id AS RoomId,i.user_id AS OwnerId,r.owner AS RoomOwner FROM items i JOIN rooms r ON r.id=i.room_id WHERE i.id=@id FOR UPDATE", new { id = box.Id }, transaction);
        if (placed == null || placed.RoomId != roomId || placed.OwnerId != box.OwnerId
            || !uint.TryParse(placed.RoomOwner, NumberStyles.None, CultureInfo.InvariantCulture, out var roomOwner) || roomOwner == 0) return new(8);
        connection.Execute("INSERT IGNORE INTO wired_reward_state(item_id,claims) VALUES (@id,'{}')", new { id = box.Id }, transaction);
        var json = connection.QuerySingle<string>("SELECT claims FROM wired_reward_state WHERE item_id=@id FOR UPDATE", new { id = box.Id }, transaction);
        var claims = WiredRewardClaimsJson.Parse(json);
        var p = configuration.IntParams;
        if (p[2] != 0 && claims.Values.Sum(claim => claim.Count) >= p[2]) return new(1);
        var claim = claims.GetValueOrDefault(habbo.Id) ?? new();
        if (!WiredRewards.IntervalOpen(claim, p[0], p[3], now)) return new(2);
        var reward = WiredRewards.Pick(prizes, p[1] == 1, claim, p[1] == 1 ? Random.Shared.Next(1, Math.Max(1, prizes.Count(entry => !claim.ReceivedCodes.Contains(entry.Code))) + 1) : Random.Shared.Next(1, 101));
        if (reward == null) return new(p[1] == 1 ? 2 : 3);
        WiredRewardGrant grant;
        if (reward.Type == 0)
        {
            var badge = connection.QuerySingleOrDefault<BadgeRow>("SELECT code AS Code,required_right AS RequiredRight FROM badge_definitions WHERE code=@code", new { code = reward.Code }, transaction);
            if (badge == null || badge.RequiredRight.Length > 0 && (habbo.Access == null || !habbo.Access.Can(badge.RequiredRight))) return new(0);
            if (connection.Execute("INSERT IGNORE INTO user_badges(user_id,badge_id,badge_slot) VALUES (@user,@code,0)", new { user = habbo.Id, code = badge.Code }, transaction) != 1) return new(2);
            grant = new(4, badge.Code);
        }
        else
        {
            ItemDefinition? definition;
            if (reward.Code.StartsWith("furni#", StringComparison.Ordinal)) definitions.Items.TryGetValue(uint.Parse(reward.Code[6..], CultureInfo.InvariantCulture), out definition);
            else definition = definitions.GetItemByName(reward.Code);
            if (definition == null) return new(0);
            connection.Execute("INSERT INTO items(base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,limited_number,limited_stack) VALUES (@definition,@user,0,0,0,0,'',0,'',0,0)", new { definition = definition.Id, user = habbo.Id }, transaction);
            var id = connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
            grant = new(5, Furniture: new() { Id = id, OwnerId = checked((uint)habbo.Id), Definition = definition, ExtraData = FurniExtraData.Load(definition, "", true) });
        }
        claim.Count++; claim.LastClaimAt = now; claim.ReceivedCodes.Add(reward.Code); claims[habbo.Id] = claim;
        connection.Execute("UPDATE wired_reward_state SET claims=@claims WHERE item_id=@id", new { id = box.Id, claims = WiredRewardClaimsJson.Serialize(claims) }, transaction);
        transaction.Commit();
        return grant;
    }
    private sealed class Placement { public uint RoomId { get; set; } public uint OwnerId { get; set; } public string RoomOwner { get; set; } = ""; }
    private sealed class BadgeRow { public string Code { get; set; } = ""; public string RequiredRight { get; set; } = ""; }
}
