using System.Collections.Immutable;
using System.Data;
using System.Globalization;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Games.SnowStorm;

/// <summary>A player's SnowStorm account: games played in total, free games used today and bought games.</summary>
public sealed record SnowStormAccount(int GamesPlayed, int FreeGamesUsedToday, int Tokens)
{
    /// <summary>Games the player can still start today; -1 when free games are unlimited.</summary>
    public int GamesLeft(int freeGamesPerDay) => freeGamesPerDay < 0 ? -1 : Math.Max(0, freeGamesPerDay - FreeGamesUsedToday) + Tokens;
}

public enum SnowStormLeaderboardKind
{
    Total,
    Friends,
    Weekly,
    WeeklyFriends,
    TotalGroup,
    WeeklyGroup
}

public sealed record SnowStormLeaderboardRequest(SnowStormLeaderboardKind Kind, int ViewerId, int WeekOffset, int StartRank, int ViewSize, int WindowSize)
{
    public bool Weekly => Kind is SnowStormLeaderboardKind.Weekly or SnowStormLeaderboardKind.WeeklyFriends or SnowStormLeaderboardKind.WeeklyGroup;

    public bool Friends => Kind is SnowStormLeaderboardKind.Friends or SnowStormLeaderboardKind.WeeklyFriends;

    public bool Group => Kind is SnowStormLeaderboardKind.TotalGroup or SnowStormLeaderboardKind.WeeklyGroup;
}

public interface ISnowStormStore
{
    SnowStormAccount GetAccount(int userId, DateOnly today);

    /// <summary>Uses one of today's free games, else one bought game. False when the player has none left.</summary>
    bool TryConsumeGame(int userId, DateOnly today, int freeGamesPerDay);

    IReadOnlyDictionary<int, int> GetTotalScores(IReadOnlyCollection<int> userIds);

    void RecordScores(DateOnly weekStart, IReadOnlyList<(int UserId, int Score)> scores);

    SnowStormLeaderboardPage LoadLeaderboard(SnowStormLeaderboardRequest request, DateTimeOffset now);

    ImmutableArray<SnowStormTokenOffer> GetOffers();

    /// <summary>Debits the offer price from the wallet and credits its games in one transaction.</summary>
    SnowStormTokenOffer? Purchase(Habbo habbo, int offerId);
}

public sealed class SnowStormStore(IDatabase database) : ISnowStormStore
{
    private const int GameTypeId = 0;
    private const int MaxPageSize = 50;

    public SnowStormAccount GetAccount(int userId, DateOnly today)
    {
        using var connection = database.Connection();
        var tokens = connection.QuerySingleOrDefault<TokenRow>("SELECT games AS Games, free_games_date AS FreeGamesDate, free_games_used AS FreeGamesUsed FROM snowwar_game_tokens WHERE user_id = @userId",
            new { userId });
        var played = connection.ExecuteScalar<long?>("SELECT SUM(matches) FROM snowwar_scores WHERE user_id = @userId", new { userId }) ?? 0;

        return new((int)Math.Min(int.MaxValue, played), tokens?.UsedOn(today) ?? 0, tokens?.Games ?? 0);
    }

    public bool TryConsumeGame(int userId, DateOnly today, int freeGamesPerDay)
    {
        if (freeGamesPerDay < 0) {
            return true;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("INSERT IGNORE INTO snowwar_game_tokens (user_id) VALUES (@userId)", new { userId }, transaction);
        var row = connection.QuerySingle<TokenRow>("SELECT games AS Games, free_games_date AS FreeGamesDate, free_games_used AS FreeGamesUsed FROM snowwar_game_tokens WHERE user_id = @userId FOR UPDATE",
            new { userId }, transaction);
        var used = row.UsedOn(today);
        var games = row.Games;

        if (used < freeGamesPerDay) {
            used++;
        }
        else if (games > 0) {
            games--;
        }
        else {
            return false;
        }

        connection.Execute("UPDATE snowwar_game_tokens SET games = @games, free_games_date = @today, free_games_used = @used WHERE user_id = @userId",
            new { userId, games, used, today = today.ToDateTime(TimeOnly.MinValue) }, transaction);
        transaction.Commit();

        return true;
    }

    public IReadOnlyDictionary<int, int> GetTotalScores(IReadOnlyCollection<int> userIds)
    {
        if (userIds.Count == 0) {
            return new Dictionary<int, int>();
        }

        using var connection = database.Connection();

        return connection.Query<(int UserId, long Score)>("SELECT user_id, SUM(score) FROM snowwar_scores WHERE user_id IN @userIds GROUP BY user_id", new { userIds })
            .ToDictionary(row => row.UserId, row => Clamp(row.Score));
    }

    public void RecordScores(DateOnly weekStart, IReadOnlyList<(int UserId, int Score)> scores)
    {
        if (scores.Count == 0) {
            return;
        }

        using var connection = database.Connection();
        connection.Execute("INSERT INTO snowwar_scores (user_id, week_start, score, matches) VALUES (@UserId, @week, @Score, 1) ON DUPLICATE KEY UPDATE score = score + VALUES(score), matches = matches + 1",
            scores.Select(score => new { score.UserId, Score = Math.Max(0, score.Score), week = weekStart.ToDateTime(TimeOnly.MinValue) }));
    }

    public SnowStormLeaderboardPage LoadLeaderboard(SnowStormLeaderboardRequest request, DateTimeOffset now)
    {
        using var connection = database.Connection();
        var current = WeekStart(now);
        var first = connection.ExecuteScalar<DateTime?>("SELECT MIN(week_start) FROM snowwar_scores");
        var maxOffset = first is { } oldest ? Math.Max(0, (current.DayNumber - DateOnly.FromDateTime(oldest).DayNumber) / 7) : 0;
        var offset = request.Weekly ? Math.Clamp(request.WeekOffset, 0, maxOffset) : 0;
        var week = current.AddDays(-7 * offset);
        var limit = Math.Clamp(Math.Max(request.ViewSize, request.WindowSize), 1, MaxPageSize);
        var scores = request.Weekly
            ? "SELECT user_id, score FROM snowwar_scores WHERE week_start = @week"
            : "SELECT user_id, SUM(score) AS score FROM snowwar_scores GROUP BY user_id";
        var ranked = request.Group
            ? $"WITH scores AS ({scores}), grouped AS (SELECT stats.groupid AS id, SUM(scores.score) AS score FROM scores INNER JOIN user_statistics stats ON stats.id = scores.user_id WHERE scores.score > 0 AND stats.groupid > 0 GROUP BY stats.groupid), " +
              "ranked AS (SELECT grouped.id, grouped.score, RANK() OVER (ORDER BY grouped.score DESC) AS position, `groups`.name, `groups`.badge AS figure, 'g' AS gender FROM grouped INNER JOIN `groups` ON `groups`.id = grouped.id)"
            : $"WITH scores AS ({scores}), ranked AS (SELECT scores.user_id AS id, scores.score, RANK() OVER (ORDER BY scores.score DESC) AS position, users.username AS name, users.look AS figure, LOWER(users.gender) AS gender " +
              "FROM scores INNER JOIN users ON users.id = scores.user_id WHERE scores.score > 0" +
              (request.Friends ? " AND (scores.user_id = @viewer OR EXISTS (SELECT 1 FROM messenger_friendships WHERE user_one_id = @viewer AND user_two_id = scores.user_id))" : "") + ")";
        var args = new { week = week.ToDateTime(TimeOnly.MinValue), viewer = request.ViewerId, limit, start = 1, focus = 0 };
        var focus = request.Group ? connection.ExecuteScalar<int?>("SELECT groupid FROM user_statistics WHERE id = @viewer", args) ?? 0 : request.ViewerId;
        var start = request.StartRank;

        // AIR asks for rank -1 to centre the table on the viewer (or their favourite group).
        if (start < 1) {
            var own = connection.ExecuteScalar<int?>(ranked + " SELECT position FROM ranked WHERE id = @focus", args with { focus = focus });
            start = own is { } rank ? Math.Max(1, rank - request.ViewSize / 2) : 1;
        }

        var entries = connection.Query<EntryRow>(ranked + " SELECT id, score, position, name, figure, gender FROM ranked WHERE position >= @start ORDER BY position, id LIMIT @limit",
            args with { start = start }).Select(row => new SnowStormLeaderboardEntry(row.Id, Clamp(row.Score), row.Position, row.Name ?? "", row.Figure ?? "", row.Gender ?? "")).ToImmutableArray();
        var total = connection.ExecuteScalar<int>(ranked + " SELECT COUNT(*) FROM ranked", args);

        return new(entries, total, GameTypeId,
            request.Weekly ? new SnowStormLeaderboardWeek(ISOWeek.GetYear(week.ToDateTime(TimeOnly.MinValue)), ISOWeek.GetWeekOfYear(week.ToDateTime(TimeOnly.MinValue)), maxOffset, offset, MinutesUntilReset(now)) : null,
            request.Group ? focus : 0);
    }

    public ImmutableArray<SnowStormTokenOffer> GetOffers()
    {
        using var connection = database.Connection();

        return connection.Query<SnowStormTokenOffer>(OfferSelect + " WHERE enabled = 1 ORDER BY order_num, id").ToImmutableArray();
    }

    public SnowStormTokenOffer? Purchase(Habbo habbo, int offerId)
    {
        lock (habbo.WalletSync) {
            if (habbo.WalletClosed) {
                return null;
            }

            using var connection = database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var offer = connection.QuerySingleOrDefault<SnowStormTokenOffer>(OfferSelect + " WHERE id = @offerId AND enabled = 1", new { offerId }, transaction);

            if (offer == null || offer.Games <= 0 || offer.PriceCredits < 0 || offer.PricePoints < 0 || offer.PricePoints > 0 && offer.PointsType is not (0 or 5)) {
                return null;
            }

            var credits = (long)habbo.Credits - offer.PriceCredits;
            var duckets = (long)habbo.Duckets - (offer.PointsType == 0 ? offer.PricePoints : 0);
            var diamonds = (long)habbo.Diamonds - (offer.PointsType == 5 ? offer.PricePoints : 0);

            if (credits < 0 || duckets < 0 || diamonds < 0 ||
                connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id = @id FOR UPDATE", new { id = habbo.Id }, transaction) == null) {
                return null;
            }

            // Debit first, then credit the games, in one transaction (Polaris credited before charging).
            connection.Execute("UPDATE users SET credits = @credits, activity_points = @duckets, vip_points = @diamonds WHERE id = @id",
                new { id = habbo.Id, credits, duckets, diamonds }, transaction);
            connection.Execute("INSERT INTO snowwar_game_tokens (user_id, games) VALUES (@id, @games) ON DUPLICATE KEY UPDATE games = games + VALUES(games)",
                new { id = habbo.Id, games = offer.Games }, transaction);
            transaction.Commit();
            habbo.Credits = (int)credits;
            habbo.Duckets = (int)duckets;
            habbo.Diamonds = (int)diamonds;

            return offer;
        }
    }

    /// <summary>Weekly tables reset on Monday 00:00 UTC.</summary>
    public static DateOnly WeekStart(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
    }

    public static int MinutesUntilReset(DateTimeOffset now) =>
        (int)Math.Max(0, (WeekStart(now).AddDays(7).ToDateTime(TimeOnly.MinValue) - now.UtcDateTime).TotalMinutes);

    private const string OfferSelect = "SELECT id AS Id, localization_id AS LocalizationId, price_credits AS PriceCredits, price_points AS PricePoints, points_type AS PointsType, games AS Games FROM snowwar_token_offers";

    private static int Clamp(long score) => (int)Math.Clamp(score, 0, int.MaxValue);

    private sealed class TokenRow
    {
        public int Games { get; set; }
        public DateTime? FreeGamesDate { get; set; }
        public int FreeGamesUsed { get; set; }

        public int UsedOn(DateOnly today) => FreeGamesDate is { } date && DateOnly.FromDateTime(date) == today ? FreeGamesUsed : 0;
    }

    private sealed class EntryRow
    {
        public int Id { get; set; }
        public long Score { get; set; }
        public int Position { get; set; }
        public string? Name { get; set; }
        public string? Figure { get; set; }
        public string? Gender { get; set; }
    }
}
