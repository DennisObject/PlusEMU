using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Plus.HabboHotel.Badges.Rarity;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.Communication.Http;

/// <summary>
/// GET /api/badges/leaderboard for the Volt badge leaderboard and rarity labels. A bearer
/// token adds the caller's own rank to each board.
/// </summary>
public class BadgeLeaderboardEndpoints(IBadgeRarityManager rarity, IAccessTokenStore accessTokens)
{
    public void Map(IEndpointRouteBuilder routes) => routes.MapGet("/api/badges/leaderboard", Leaderboard);

    private async Task<IResult> Leaderboard(HttpRequest request)
    {
        var snapshot = rarity.Snapshot;
        var token = AuthEndpoints.BearerToken(request);
        var viewerId = token == null ? 0 : await accessTokens.FindUser(token) ?? 0;
        var viewer = viewerId > 0 ? await rarity.GetProfile(viewerId) : null;
        var scale = snapshot.Rarity.Scale;

        return Results.Json(new
        {
            viewerUserId = viewerId,
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
                totalBadges = Board(snapshot, snapshot.TotalBadges, viewerId, viewer),
                achievementLevel = Board(snapshot, snapshot.AchievementLevel, viewerId, viewer),
                rarity = snapshot.RarityBoards.OrderBy(board => board.Key)
                    .ToDictionary(board => BadgeRarityScale.Key(board.Key), board => Board(snapshot, board.Value, viewerId, viewer))
            }
        });
    }

    private static Dictionary<string, object> Board(BadgeLeaderboardSnapshot snapshot, LeaderboardBoard board, int viewerId, LeaderboardProfile? viewer)
    {
        var result = new Dictionary<string, object>
        {
            ["entries"] = board.Top.Select((score, index) => Entry(score.UserId, index + 1, score.Score, snapshot.Profiles.GetValueOrDefault(score.UserId))),
            // Volt pages through the entries it was sent, so the total counts those only.
            ["totalPlayers"] = board.Top.Count()
        };

        if (viewer != null && board.Find(viewerId) is { } own) {
            result["viewerEntry"] = Entry(viewerId, own.Rank, own.Score, viewer);
        }

        return result;
    }

    private static object Entry(int userId, int rank, int score, LeaderboardProfile? profile) =>
        new { userId, username = profile?.Username ?? "", figure = profile?.Figure ?? "", score, rank };
}
