using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Core;
using Plus.Core.Settings;
using Plus.Database;

namespace Plus.HabboHotel.Badges.Rarity;

public interface IBadgeRarityManager
{
    BadgeLeaderboardSnapshot Snapshot { get; }

    /// <summary>Rebuilds owner counts, rarity tiers and leaderboards from the database.</summary>
    Task Refresh();

    Task<LeaderboardProfile?> GetProfile(int userId);
}

/// <summary>
/// Recounts badge owners every ten minutes. Players count as active when they are online or
/// were online within badge.rarity.active_days, and the tier ceilings scale with that number.
/// </summary>
public sealed class BadgeRarityManager(IDatabase database, ISettingsManager settings, TimeProvider time, ILogger<BadgeRarityManager> logger)
    : IBadgeRarityManager, IStartable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    private BadgeLeaderboardSnapshot _snapshot = BadgeLeaderboardSnapshot.Empty;

    public BadgeLeaderboardSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public async Task Start()
    {
        try {
            await Refresh();
        }
        catch (Exception e) {
            logger.LogWarning(e, "Badge rarity refresh failed; badges report no owners until the next refresh.");
        }

        _ = Task.Run(RunForever);
    }

    public async Task Refresh()
    {
        using var connection = database.Connection();
        var since = time.GetUtcNow().UtcDateTime.AddDays(-BadgeRarityScale.ActiveDays(settings));
        var population = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM users WHERE online = 1 OR last_online >= @since", new { since });
        var ownership = await connection.QueryAsync<Row>(
            "SELECT b.user_id AS UserId, b.badge_id AS Badge FROM user_badges b JOIN users u ON u.id = b.user_id");
        var achievements = await connection.QueryAsync<Row>(
            "SELECT s.id AS UserId, s.AchievementScore AS Score FROM user_statistics s JOIN users u ON u.id = s.id WHERE s.AchievementScore > 0");
        var snapshot = BadgeLeaderboardSnapshot.Build(ownership.Select(row => new BadgeOwnership(row.UserId, row.Badge)),
            achievements.Select(row => new LeaderboardScore(row.UserId, row.Score)), BadgeRarityScale.For(population, settings));
        var ids = snapshot.TopUserIds.ToArray();
        var profiles = ids.Length == 0
            ? new Dictionary<int, LeaderboardProfile>()
            : (await connection.QueryAsync<Row>("SELECT id AS UserId, username AS Username, look AS Look FROM users WHERE id IN @ids", new { ids }))
            .ToDictionary(user => user.UserId, user => user.Profile);

        snapshot = snapshot.WithProfiles(profiles);
        Volatile.Write(ref _snapshot, snapshot);
        BadgeRarityTable.Current = snapshot.Rarity;
    }

    public async Task<LeaderboardProfile?> GetProfile(int userId)
    {
        if (Snapshot.Profiles.TryGetValue(userId, out var profile)) {
            return profile;
        }

        using var connection = database.Connection();
        var user = await connection.QuerySingleOrDefaultAsync<Row>(
            "SELECT id AS UserId, username AS Username, look AS Look FROM users WHERE id = @userId", new { userId });

        return user?.Profile;
    }

    private async Task RunForever()
    {
        using var timer = new PeriodicTimer(Interval, time);

        while (await timer.WaitForNextTickAsync()) {
            try {
                await Refresh();
            }
            catch (Exception e) {
                logger.LogWarning(e, "Badge rarity refresh failed; keeping the previous counts for {Interval}.", Interval);
            }
        }
    }

    // Dapper converts column types only through property setters; user_badges.user_id is unsigned.
    private sealed class Row
    {
        public int UserId { get; set; }
        public string Badge { get; set; } = "";
        public int Score { get; set; }
        public string? Username { get; set; }
        public string? Look { get; set; }

        public LeaderboardProfile Profile => new(Username ?? "", Look ?? "");
    }
}
