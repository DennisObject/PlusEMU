using System.Collections.Immutable;
using System.Globalization;
using Plus.Core.Settings;

namespace Plus.HabboHotel.Badges.Rarity;

/// <summary>WIN63 badge rarity ids (class_3472). Legendary outranks mythical.</summary>
public enum BadgeRarityTier
{
    Common = 0,
    Uncommon = 1,
    Rare = 2,
    Epic = 3,
    Mythical = 4,
    Legendary = 5,
    Unique = 6
}

/// <summary>What a badge packet sends next to the badge code.</summary>
public readonly record struct BadgeRarity(int OwnerCount, BadgeRarityTier Tier);

/// <summary>
/// Owner ceilings for the active population. A tier holds badges with at most
/// max(min_owners, ceil(percent of active players)) owners, so the bands grow with the hotel.
/// A badge with exactly one owner is always unique.
/// </summary>
public sealed record BadgeRarityScale(int Population, ImmutableArray<(BadgeRarityTier Tier, int MaxOwners)> Ceilings)
{
    public const int DefaultActiveDays = 90;

    // Rarest first. The minimums equal the old fixed bands, so a small hotel ranks badges as before.
    private static readonly (BadgeRarityTier Tier, double Percent, int MinOwners)[] Defaults =
    [
        (BadgeRarityTier.Legendary, 0.5, 3),
        (BadgeRarityTier.Mythical, 1.5, 8),
        (BadgeRarityTier.Epic, 5, 20),
        (BadgeRarityTier.Rare, 15, 50),
        (BadgeRarityTier.Uncommon, 35, 0)
    ];

    public static BadgeRarityScale Empty { get; } = For(0, null);

    public static BadgeRarityScale For(int population, ISettingsManager? settings)
    {
        var uncommon = settings?.GetOptionalValue("badge.rarity.uncommon") == "1";
        var ceilings = Defaults.Where(band => band.Tier != BadgeRarityTier.Uncommon || uncommon).Select(band =>
        {
            var key = "badge.rarity." + Key(band.Tier);
            var percent = Read(settings, key + ".percent", band.Percent);
            var minimum = (int)Read(settings, key + ".min_owners", band.MinOwners);

            return (band.Tier, Math.Max(minimum, (int)Math.Ceiling(percent / 100 * population)));
        });

        return new(population, ceilings.ToImmutableArray());
    }

    public static int ActiveDays(ISettingsManager settings) => (int)Read(settings, "badge.rarity.active_days", DefaultActiveDays);

    public BadgeRarityTier Classify(int owners)
    {
        if (owners <= 0) {
            return BadgeRarityTier.Common;
        }

        if (owners == 1) {
            return BadgeRarityTier.Unique;
        }

        foreach (var (tier, maxOwners) in Ceilings) {
            if (owners <= maxOwners) {
                return tier;
            }
        }

        return BadgeRarityTier.Common;
    }

    /// <summary>Tiers with a leaderboard, in the order WIN63 lists them (uncommon only when enabled).</summary>
    public IEnumerable<BadgeRarityTier> LeaderboardTiers() =>
        Ceilings.Select(ceiling => ceiling.Tier).Append(BadgeRarityTier.Unique).OrderBy(tier => tier);

    public static string Key(BadgeRarityTier tier) => tier.ToString().ToLowerInvariant();

    private static double Read(ISettingsManager? settings, string key, double fallback) =>
        double.TryParse(settings?.GetOptionalValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value >= 0 ? value : fallback;
}

/// <summary>Owner counts per badge code, classified against one scale.</summary>
public sealed class BadgeRarityTable(BadgeRarityScale scale, IReadOnlyDictionary<string, int> owners)
{
    private static BadgeRarityTable _current = new(BadgeRarityScale.Empty, new Dictionary<string, int>());

    /// <summary>The latest refresh. Packet snapshots read it once when they are captured.</summary>
    public static BadgeRarityTable Current
    {
        get => Volatile.Read(ref _current);
        internal set => Volatile.Write(ref _current, value);
    }

    public BadgeRarityScale Scale => scale;

    /// <summary>Rarity of a badge someone holds. A badge newer than the last refresh counts its holder.</summary>
    public BadgeRarity Get(string code)
    {
        var count = Math.Max(1, owners.GetValueOrDefault(code));

        return new(count, scale.Classify(count));
    }
}
