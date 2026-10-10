using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Plus.HabboHotel.Badges.Rarity;

namespace Plus.Communication.Http;

/// <summary>GET /api/badges/leaderboard for badge rarity labels. The board is anonymous.</summary>
public class BadgeLeaderboardEndpoints(IBadgeRarityManager rarity)
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/badges/leaderboard", Leaderboard);

    private IResult Leaderboard()
    {
        var snapshot = rarity.Snapshot;
        var scale = snapshot.Rarity.Scale;

        return Results.Json(new
        {
            viewerUserId = 0,
            population = scale.Population,
            thresholds = scale.Ceilings.Append((Tier: BadgeRarityTier.Unique, MaxOwners: 1))
                .ToDictionary(ceiling => BadgeRarityScale.Key(ceiling.Tier), ceiling => ceiling.MaxOwners),
            badgeStats = snapshot.Badges.Select(badge => new
            {
                badgeCode = badge.Badge,
                ownerCount = badge.Owners,
                rarity = BadgeRarityScale.Key(scale.Classify(badge.Owners))
            }),
            leaderboards = new
            {
                totalBadges = Board(snapshot, snapshot.TotalBadges),
                achievementLevel = Board(snapshot, snapshot.AchievementLevel),
                rarity = snapshot.RarityBoards.OrderBy(board => board.Key)
                    .ToDictionary(board => BadgeRarityScale.Key(board.Key), board => Board(snapshot, board.Value))
            }
        });
    }

    private static Dictionary<string, object> Board(BadgeLeaderboardSnapshot snapshot, LeaderboardBoard board) => new()
    {
        ["entries"] = board.Top.Select((score, index) => Entry(score.UserId, index + 1, score.Score, snapshot.Profiles.GetValueOrDefault(score.UserId))),
        // Clients page through the entries they were sent, so the total counts those only.
        ["totalPlayers"] = board.Top.Count()
    };

    private static object Entry(int userId, int rank, int score, LeaderboardProfile? profile) =>
        new { userId, username = profile?.Username ?? "", figure = profile?.Figure ?? "", score, rank };
}
