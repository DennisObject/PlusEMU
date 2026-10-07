using System.Collections.Immutable;

namespace Plus.HabboHotel.Badges.Rarity;

public sealed record BadgeOwnership(int UserId, string Badge);
public sealed record LeaderboardScore(int UserId, int Score);
public sealed record LeaderboardProfile(string Username, string Figure);

/// <summary>Users ordered by score, highest first; ties go to the lower user id.</summary>
public sealed class LeaderboardBoard
{
    private readonly Dictionary<int, int> _ranks;

    public LeaderboardBoard(IEnumerable<LeaderboardScore> scores)
    {
        Ranked = scores.Where(score => score.Score > 0).OrderByDescending(score => score.Score).ThenBy(score => score.UserId).ToImmutableArray();
        _ranks = new(Ranked.Length);

        for (var i = 0; i < Ranked.Length; i++) {
            _ranks[Ranked[i].UserId] = i + 1;
        }
    }

    public ImmutableArray<LeaderboardScore> Ranked { get; }

    public IEnumerable<LeaderboardScore> Top => Ranked.Take(BadgeLeaderboardSnapshot.EntryLimit);

    /// <summary>1-based rank and score, or null when the user is not on this board.</summary>
    public (int Rank, int Score)? Find(int userId) =>
        _ranks.TryGetValue(userId, out var rank) ? (rank, Ranked[rank - 1].Score) : null;
}

/// <summary>Badge owner counts and every leaderboard, built from one read of the database.</summary>
public sealed class BadgeLeaderboardSnapshot
{
    public const int EntryLimit = 100;

    private BadgeLeaderboardSnapshot(BadgeRarityTable rarity, ImmutableArray<(string Badge, int Owners)> badges, LeaderboardBoard totalBadges,
        LeaderboardBoard achievementLevel, ImmutableDictionary<BadgeRarityTier, LeaderboardBoard> rarityBoards,
        IReadOnlyDictionary<int, LeaderboardProfile> profiles)
    {
        Rarity = rarity;
        Badges = badges;
        TotalBadges = totalBadges;
        AchievementLevel = achievementLevel;
        RarityBoards = rarityBoards;
        Profiles = profiles;
    }

    public static BadgeLeaderboardSnapshot Empty { get; } = Build([], [], BadgeRarityScale.Empty);

    public BadgeRarityTable Rarity { get; }
    public ImmutableArray<(string Badge, int Owners)> Badges { get; }
    public LeaderboardBoard TotalBadges { get; }
    public LeaderboardBoard AchievementLevel { get; }
    public ImmutableDictionary<BadgeRarityTier, LeaderboardBoard> RarityBoards { get; }

    /// <summary>Names and figures of <see cref="TopUserIds"/>.</summary>
    public IReadOnlyDictionary<int, LeaderboardProfile> Profiles { get; }

    /// <summary>Profiles the top entries need; load these before publishing the snapshot.</summary>
    public IEnumerable<int> TopUserIds => RarityBoards.Values.Append(TotalBadges).Append(AchievementLevel)
        .SelectMany(board => board.Top).Select(score => score.UserId).Distinct();

    public BadgeLeaderboardSnapshot WithProfiles(IReadOnlyDictionary<int, LeaderboardProfile> profiles) =>
        new(Rarity, Badges, TotalBadges, AchievementLevel, RarityBoards, profiles);

    public static BadgeLeaderboardSnapshot Build(IEnumerable<BadgeOwnership> ownership, IEnumerable<LeaderboardScore> achievements, BadgeRarityScale scale)
    {
        // user_badges compares badge codes case-insensitively, so owner counts do too.
        var owners = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var badgesByUser = new Dictionary<int, List<string>>();

        foreach (var row in ownership) {
            owners[row.Badge] = owners.GetValueOrDefault(row.Badge) + 1;

            if (!badgesByUser.TryGetValue(row.UserId, out var codes)) {
                badgesByUser[row.UserId] = codes = [];
            }

            codes.Add(row.Badge);
        }

        var tiers = scale.LeaderboardTiers().ToArray();
        var tierScores = tiers.ToDictionary(tier => tier, _ => new List<LeaderboardScore>());

        foreach (var (userId, codes) in badgesByUser) {
            foreach (var group in codes.GroupBy(code => scale.Classify(owners[code]))) {
                if (tierScores.TryGetValue(group.Key, out var scores)) {
                    scores.Add(new(userId, group.Count()));
                }
            }
        }

        return new(new BadgeRarityTable(scale, owners),
            owners.OrderBy(owner => owner.Key, StringComparer.Ordinal).Select(owner => (owner.Key, owner.Value)).ToImmutableArray(),
            new(badgesByUser.Select(user => new LeaderboardScore(user.Key, user.Value.Count))),
            new(achievements),
            tierScores.ToImmutableDictionary(tier => tier.Key, tier => new LeaderboardBoard(tier.Value)),
            new Dictionary<int, LeaderboardProfile>());
    }
}
