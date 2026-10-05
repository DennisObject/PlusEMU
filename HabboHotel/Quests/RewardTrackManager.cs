using Plus.HabboHotel.Users.Inventory.Badges;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Inventory.Badges;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Communication.Packets.Outgoing.Quests;
using Plus.Core;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Badges;
using Plus.Utilities;

namespace Plus.HabboHotel.Quests;

public sealed class RewardTrackManager : IRewardTrackManager, IStartable
{
    public static RewardTrackManager? Current { get; private set; }

    private readonly ILogger<RewardTrackManager> _logger;
    private readonly IDatabase _database;
    private readonly IBadgeManager _badgeManager;
    private readonly object _definitions = new();
    private readonly object _gates = new();
    private readonly Dictionary<int, object> _userGates = new();
    private readonly Dictionary<int, Dictionary<string, UserRewardTrackState>> _users = new();
    private readonly HashSet<int> _loaded = new();
    private List<RewardTrack> _tracks = new();

    public RewardTrackManager(ILogger<RewardTrackManager> logger, IDatabase database, IBadgeManager badgeManager)
    {
        _logger = logger;
        _database = database;
        _badgeManager = badgeManager;
        Current = this;
    }

    public int StartOrder => 30;

    public async Task Start()
    {
        try
        {
            var tracks = await LoadDefinitions();
            lock (_definitions)
                _tracks = tracks;
            _logger.LogInformation("Loaded {Count} reward tracks.", tracks.Count);
        }
        catch (Exception exception)
        {
            lock (_definitions)
                _tracks = new();
            _logger.LogError(exception, "Reward track definitions failed to load.");
        }
    }

    public void Progress(GameClient session, string actionType, int amount = 1)
    {
        if (session == null)
            return;
        var habbo = session.GetHabbo();
        if (habbo == null || amount < 1 || string.IsNullOrEmpty(actionType))
            return;
        var tracks = Snapshot();
        if (tracks.Count == 0)
            return;
        var now = (int)UnixTimestamp.GetNow();
        lock (Gate(habbo.Id))
        {
            if (!EnsureUser(habbo.Id))
                return;
            foreach (var track in tracks)
            {
                if (!track.IsActive(now))
                    continue;
                var state = StateFor(habbo.Id, track.Id);
                foreach (var task in track.Tasks)
                {
                    if (!string.Equals(task.ActionType, actionType, StringComparison.Ordinal))
                        continue;
                    var step = RewardTrackRules.Plan(track, state, task, amount);
                    if (!step.Changed)
                        continue;
                    if (!SaveProgress(habbo.Id, track.Id, step, state.Premium))
                        continue;
                    state.Apply(step);
                    SendTrackPacket(session, new RewardTrackProgressComposer(track.Id, step.TaskId, step.Count, state.Points));
                    _logger.LogInformation("Reward track progress user {UserId} task {TaskId} count {Count} points {Points}", habbo.Id, step.TaskId, step.Count, step.PointsGranted);
                }
            }
        }
    }

    public void SendTracks(GameClient session)
    {
        if (session == null)
            return;
        var habbo = session.GetHabbo();
        if (habbo == null)
            return;
        var tracks = Snapshot();
        var now = (int)UnixTimestamp.GetNow();
        lock (Gate(habbo.Id))
        {
            if (tracks.Count > 0 && !EnsureUser(habbo.Id))
                return;
            var views = new List<RewardTrackView>();
            foreach (var track in tracks)
            {
                if (!track.IsActive(now))
                    continue;
                views.Add(new RewardTrackView(track, StateFor(habbo.Id, track.Id)));
            }
            SendTrackPacket(session, new RewardTracksComposer(false, views, false));
        }
    }

    public Task Claim(GameClient session, string trackId, string prizeId)
    {
        if (session == null)
            return Task.CompletedTask;
        var habbo = session.GetHabbo();
        if (habbo == null)
            return Task.CompletedTask;
        lock (Gate(habbo.Id))
        {
            if (!EnsureUser(habbo.Id))
            {
                _logger.LogError("Reward track claim load failed for user {UserId}", habbo.Id);
                return Task.CompletedTask;
            }
            var track = FindActive(Snapshot(), trackId);
            var state = track == null ? null : StateFor(habbo.Id, track.Id);
            var result = RewardTrackRules.PreviewClaim(track, state, prizeId);
            if (result != RewardTrackResults.Ok || track == null || state == null)
            {
                LogClaim(habbo.Id, trackId, prizeId, result);
                SendTrackPacket(session, new RewardTrackClaimResultComposer(trackId, prizeId, result));
                return Task.CompletedTask;
            }
            var prize = track.GetPrize(prizeId);
            if (prize == null)
            {
                LogClaim(habbo.Id, trackId, prizeId, RewardTrackResults.Unknown);
                SendTrackPacket(session, new RewardTrackClaimResultComposer(trackId, prizeId, RewardTrackResults.Unknown));
                return Task.CompletedTask;
            }
            string? badge = null;
            var credits = 0;
            var duckets = 0;
            var diamonds = 0;
            switch (prize.RewardType.Trim().ToLowerInvariant())
            {
                case "badge":
                    badge = prize.ExtraParams;
                    break;
                case "credits":
                    credits = prize.RewardAmount;
                    break;
                case "duckets":
                    duckets = prize.RewardAmount;
                    break;
                case "diamonds":
                    diamonds = prize.RewardAmount;
                    break;
            }
            // A badge prize without a grantable definition would commit a claim that never pays out.
            if (badge != null && !TryResolveBadge(habbo, badge, out badge))
            {
                _logger.LogError("Reward track prize {PrizeId} badge {Badge} has no grantable definition", prize.Id, prize.ExtraParams);
                LogClaim(habbo.Id, trackId, prizeId, RewardTrackResults.Unknown);
                SendTrackPacket(session, new RewardTrackClaimResultComposer(trackId, prizeId, RewardTrackResults.Unknown));
                return Task.CompletedTask;
            }
            if (!StoreClaim(habbo, track.Id, prize.Id, credits, duckets, diamonds, badge))
            {
                // Nothing was committed; the prize stays claimable so the player can retry.
                LogClaim(habbo.Id, trackId, prizeId, RewardTrackResults.Unknown);
                SendTrackPacket(session, new RewardTrackClaimResultComposer(trackId, prizeId, RewardTrackResults.Unknown));
                return Task.CompletedTask;
            }
            state.MarkClaimed(prize.Id);
            if (badge != null && !habbo.Inventory.Badges.HasBadge(badge))
            {
                habbo.Inventory.Badges.AddBadge(new Badge(badge, 0));
                session.Send(new BadgesComposer(BadgeInventorySnapshot.Capture(habbo.Inventory.Badges.Badges.Values)));
                session.Send(new FurniListNotificationComposer(1, 4));
            }
            LogClaim(habbo.Id, trackId, prizeId, RewardTrackResults.Ok);
            SendTrackPacket(session, new RewardTrackClaimResultComposer(trackId, prizeId, RewardTrackResults.Ok));
            if (credits > 0)
                session.Send(new CreditBalanceComposer(habbo.Credits));
            if (duckets > 0)
                session.Send(new HabboActivityPointNotificationComposer(habbo.Duckets, duckets));
            if (diamonds > 0)
                session.Send(new HabboActivityPointNotificationComposer(habbo.Diamonds, diamonds, 5));
        }
        return Task.CompletedTask;
    }

    public void PurchasePremium(GameClient session, string trackId)
    {
        if (session == null || string.IsNullOrEmpty(trackId))
            return;
        var habbo = session.GetHabbo();
        if (habbo == null)
            return;
        lock (Gate(habbo.Id))
        {
            if (!EnsureUser(habbo.Id))
            {
                _logger.LogError("Reward track premium load failed for user {UserId}", habbo.Id);
                return;
            }
            var track = FindActive(Snapshot(), trackId);
            var state = track == null ? null : StateFor(habbo.Id, track.Id);
            lock (habbo.WalletSync)
            {
                if (habbo.WalletClosed)
                {
                    _logger.LogError("Reward track premium skipped because the wallet is closed for user {UserId}", habbo.Id);
                    return;
                }
                var quote = RewardTrackRules.Quote(track, state, habbo.Credits, habbo.Diamonds);
                if (quote.Result != RewardTrackResults.Ok || track == null || state == null)
                {
                    LogPremium(habbo.Id, trackId, quote.Result);
                    SendTrackPacket(session, new RewardTrackPremiumPurchaseResultComposer(trackId, quote.Result, quote.Points));
                    return;
                }
                var costCredits = Math.Max(0, track.PremiumCostCredits);
                var costDiamonds = Math.Max(0, track.PremiumCostDiamonds);
                if (!TryBuyPremium(habbo.Id, track.Id, costCredits, costDiamonds, quote.Points, out var denied))
                {
                    if (!denied)
                        return;
                    LogPremium(habbo.Id, trackId, RewardTrackResults.NotEnoughCurrency);
                    SendTrackPacket(session, new RewardTrackPremiumPurchaseResultComposer(trackId, RewardTrackResults.NotEnoughCurrency, state.Points));
                    return;
                }
                habbo.Credits = quote.Credits;
                habbo.Diamonds = quote.Diamonds;
                state.ApplyPremium(quote.Points);
                if (costCredits > 0)
                    session.Send(new CreditBalanceComposer(habbo.Credits));
                if (costDiamonds > 0)
                    session.Send(new HabboActivityPointNotificationComposer(habbo.Diamonds, -costDiamonds, 5));
                LogPremium(habbo.Id, trackId, RewardTrackResults.Ok);
                SendTrackPacket(session, new RewardTrackPremiumPurchaseResultComposer(trackId, RewardTrackResults.Ok, state.Points));
            }
        }
    }

    private bool TryResolveBadge(Habbo habbo, string code, out string badge)
    {
        badge = code;
        if (!_badgeManager.Badges.TryGetValue(code.ToUpper(), out var definition))
            return false;
        if (definition.RequiredRight.Length > 0 && !habbo.Access.Can(definition.RequiredRight))
            return false;
        badge = definition.Code;
        return true;
    }

    private bool StoreClaim(Habbo habbo, string trackId, string prizeId, int credits, int duckets, int diamonds, string? badge)
    {
        var currency = credits != 0 || duckets != 0 || diamonds != 0;
        if (currency)
        {
            lock (habbo.WalletSync)
            {
                if (habbo.WalletClosed)
                {
                    _logger.LogError("Reward track claim skipped because the wallet is closed for user {UserId}", habbo.Id);
                    return false;
                }
                if (!InsertClaim(habbo.Id, trackId, prizeId, credits, duckets, diamonds, badge))
                    return false;
                if (credits != 0)
                    habbo.Credits += credits;
                if (duckets != 0)
                    habbo.Duckets += duckets;
                if (diamonds != 0)
                    habbo.Diamonds += diamonds;
                return true;
            }
        }
        return InsertClaim(habbo.Id, trackId, prizeId, 0, 0, 0, badge);
    }

    private bool InsertClaim(int userId, string trackId, string prizeId, int credits, int duckets, int diamonds, string? badge)
    {
        try
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute(
                "INSERT INTO users_reward_track_prizes (user_id, track_id, prize_id, claimed_at) VALUES (@userId, @trackId, @prizeId, @claimedAt)",
                new { userId, trackId, prizeId, claimedAt = (int)UnixTimestamp.GetNow() }, transaction);
            if (credits != 0 || duckets != 0 || diamonds != 0)
            {
                connection.Execute(
                    "UPDATE users SET credits = credits + @credits, activity_points = activity_points + @duckets, vip_points = vip_points + @diamonds WHERE id = @userId",
                    new { userId, credits, duckets, diamonds }, transaction);
            }
            if (badge != null)
            {
                // Same transaction as the claim row; an owned badge keeps its slot.
                connection.Execute(
                    "INSERT INTO user_badges (user_id, badge_id, badge_slot) VALUES (@userId, @badge, 0) ON DUPLICATE KEY UPDATE badge_slot = badge_slot",
                    new { userId, badge }, transaction);
            }
            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reward track claim write failed for user {UserId} prize {PrizeId}", userId, prizeId);
            return false;
        }
    }

    private bool TryBuyPremium(int userId, string trackId, int credits, int diamonds, int points, out bool denied)
    {
        denied = false;
        try
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            var row = connection.QuerySingleOrDefault<WalletRow>(
                "SELECT credits AS Credits, vip_points AS Diamonds FROM users WHERE id = @userId FOR UPDATE",
                new { userId }, transaction);
            if (row == null || row.Credits < credits || row.Diamonds < diamonds)
            {
                denied = true;
                return false;
            }
            connection.Execute(
                "UPDATE users SET credits = credits - @credits, vip_points = vip_points - @diamonds WHERE id = @userId",
                new { userId, credits, diamonds }, transaction);
            connection.Execute(
                """
                INSERT INTO users_reward_tracks (user_id, track_id, points, premium)
                VALUES (@userId, @trackId, @points, 1)
                ON DUPLICATE KEY UPDATE points = VALUES(points), premium = 1
                """,
                new { userId, trackId, points }, transaction);
            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reward track premium write failed for user {UserId} track {TrackId}", userId, trackId);
            return false;
        }
    }

    private bool SaveProgress(int userId, string trackId, RewardTrackStep step, bool premium)
    {
        try
        {
            using var connection = _database.Connection();
            connection.Open();
            using var transaction = connection.BeginTransaction();
            connection.Execute(
                """
                INSERT INTO users_reward_track_tasks (user_id, track_id, task_id, progress_count)
                VALUES (@userId, @trackId, @taskId, @count)
                ON DUPLICATE KEY UPDATE progress_count = VALUES(progress_count)
                """,
                new { userId, trackId, taskId = step.TaskId, count = step.Count }, transaction);
            if (step.PointsGranted > 0)
            {
                connection.Execute(
                    """
                    INSERT INTO users_reward_tracks (user_id, track_id, points, premium)
                    VALUES (@userId, @trackId, @points, @premium)
                    ON DUPLICATE KEY UPDATE points = VALUES(points)
                    """,
                    new { userId, trackId, points = step.TotalPoints, premium }, transaction);
            }
            transaction.Commit();
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reward track progress write failed for user {UserId} task {TaskId}", userId, step.TaskId);
            return false;
        }
    }

    private bool EnsureUser(int userId)
    {
        if (_loaded.Contains(userId))
            return true;
        try
        {
            using var connection = _database.Connection();
            var points = connection.Query<UserTrackRow>(
                "SELECT track_id AS TrackId, points AS Points, premium <> 0 AS Premium FROM users_reward_tracks WHERE user_id = @userId",
                new { userId });
            var tasks = connection.Query<UserTaskRow>(
                "SELECT track_id AS TrackId, task_id AS TaskId, progress_count AS ProgressCount FROM users_reward_track_tasks WHERE user_id = @userId",
                new { userId });
            var prizes = connection.Query<UserPrizeRow>(
                "SELECT track_id AS TrackId, prize_id AS PrizeId FROM users_reward_track_prizes WHERE user_id = @userId",
                new { userId });
            var states = new Dictionary<string, UserRewardTrackState>(StringComparer.Ordinal);
            foreach (var row in points)
                states[row.TrackId] = new UserRewardTrackState(row.TrackId, row.Points, row.Premium != 0);
            foreach (var row in tasks)
            {
                if (!states.TryGetValue(row.TrackId, out var state))
                {
                    state = new UserRewardTrackState(row.TrackId, 0, false);
                    states[row.TrackId] = state;
                }
                state.SetStoredCount(row.TaskId, row.ProgressCount);
                state.SetPeak(row.TaskId, row.ProgressCount);
            }
            foreach (var row in prizes)
            {
                if (!states.TryGetValue(row.TrackId, out var state))
                {
                    state = new UserRewardTrackState(row.TrackId, 0, false);
                    states[row.TrackId] = state;
                }
                state.MarkClaimed(row.PrizeId);
            }
            _users[userId] = states;
            _loaded.Add(userId);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Reward track progress load failed for user {UserId}", userId);
            return false;
        }
    }

    private async Task<List<RewardTrack>> LoadDefinitions()
    {
        using var connection = _database.Connection();
        var trackRows = (await connection.QueryAsync<TrackRow>(
            """
            SELECT id AS Id, theme AS Theme, sort_order AS SortOrder, starts_at AS StartsAt, ends_at AS EndsAt,
                   has_premium <> 0 AS HasPremium, premium_task_points_boost AS Boost,
                   premium_instant_points AS InstantPoints, premium_cost_diamonds AS CostDiamonds,
                   premium_cost_credits AS CostCredits
            FROM reward_tracks WHERE enabled <> 0
            """)).ToList();
        var taskRows = (await connection.QueryAsync<TaskRow>(
            """
            SELECT track_id AS TrackId, id AS Id, action_type AS ActionType, parameter AS Parameter,
                   premium <> 0 AS Premium, sort_order AS SortOrder
            FROM reward_track_tasks
            """)).ToList();
        var levelRows = (await connection.QueryAsync<LevelRow>(
            """
            SELECT track_id AS TrackId, task_id AS TaskId, level AS Level, required_count AS RequiredCount,
                   points_reward AS PointsReward, premium <> 0 AS Premium
            FROM reward_track_task_levels
            """)).ToList();
        var prizeRows = (await connection.QueryAsync<PrizeRow>(
            """
            SELECT track_id AS TrackId, id AS Id, required_points AS RequiredPoints,
                   product_item_type_id AS ProductItemTypeId, reward_type AS RewardType,
                   extra_params AS ExtraParams, reward_amount AS RewardAmount,
                   premium <> 0 AS Premium, sort_order AS SortOrder
            FROM reward_track_prizes
            """)).ToList();
        var tracks = new Dictionary<string, RewardTrack>(StringComparer.Ordinal);
        foreach (var row in trackRows)
        {
            tracks[row.Id] = new RewardTrack(row.Id, row.Theme, row.SortOrder, row.StartsAt, row.EndsAt, row.HasPremium != 0, row.Boost, row.InstantPoints, row.CostDiamonds, row.CostCredits);
        }
        var levels = levelRows.GroupBy(row => (row.TrackId, row.TaskId)).ToDictionary(
            group => group.Key,
            group => (IReadOnlyList<RewardTrackLevel>)group.OrderBy(row => row.Level).Select(row => new RewardTrackLevel(row.RequiredCount, row.PointsReward, row.Premium != 0)).ToList());
        foreach (var row in taskRows)
        {
            if (!tracks.TryGetValue(row.TrackId, out var track))
                continue;
            levels.TryGetValue((row.TrackId, row.Id), out var taskLevels);
            track.AddTask(new RewardTrackTask(row.Id, row.ActionType, row.Parameter ?? "", row.Premium != 0, row.SortOrder, taskLevels ?? Array.Empty<RewardTrackLevel>()));
        }
        foreach (var row in prizeRows)
        {
            if (!tracks.TryGetValue(row.TrackId, out var track))
                continue;
            track.AddPrize(new RewardTrackPrize(row.Id, row.RequiredPoints, row.ProductItemTypeId, row.RewardType, row.ExtraParams ?? "", row.RewardAmount, row.Premium != 0, row.SortOrder));
        }
        return tracks.Values.OrderBy(track => track.SortOrder).ThenBy(track => track.Id, StringComparer.Ordinal).ToList();
    }

    private List<RewardTrack> Snapshot()
    {
        lock (_definitions)
            return _tracks;
    }

    private static RewardTrack? FindActive(List<RewardTrack> tracks, string trackId)
    {
        var now = (int)UnixTimestamp.GetNow();
        foreach (var track in tracks)
        {
            if (track.Id == trackId && track.IsActive(now))
                return track;
        }
        return null;
    }

    private UserRewardTrackState StateFor(int userId, string trackId)
    {
        if (!_users.TryGetValue(userId, out var states))
        {
            states = new Dictionary<string, UserRewardTrackState>(StringComparer.Ordinal);
            _users[userId] = states;
        }
        if (!states.TryGetValue(trackId, out var state))
        {
            state = new UserRewardTrackState(trackId, 0, false);
            states[trackId] = state;
        }
        return state;
    }

    private object Gate(int userId)
    {
        lock (_gates)
        {
            if (!_userGates.TryGetValue(userId, out var gate))
            {
                gate = new object();
                _userGates[userId] = gate;
            }
            return gate;
        }
    }

    // A revision without reward-track headers (NITRO-1-6-6) can't take these packets; sending one would throw
    // inside the login handler and drop the client.
    private static void SendTrackPacket(GameClient session, IServerPacket composer)
    {
        if (session.Revision?.InternalIdToOutgoingIdMapping?.ContainsKey(composer.MessageId) == true)
            session.Send(composer);
    }

    private void LogClaim(int userId, string trackId, string prizeId, RewardTrackResults result) =>
        _logger.LogInformation("Reward track claim user {UserId} track {TrackId} prize {PrizeId} result {Result}", userId, trackId, prizeId, result);

    private void LogPremium(int userId, string trackId, RewardTrackResults result) =>
        _logger.LogInformation("Reward track premium user {UserId} track {TrackId} result {Result}", userId, trackId, result);

    private sealed class TrackRow
    {
        public string Id { get; set; } = "";
        public string Theme { get; set; } = "";
        public int SortOrder { get; set; }
        public int StartsAt { get; set; }
        public int EndsAt { get; set; }
        public int HasPremium { get; set; }
        public double Boost { get; set; }
        public int InstantPoints { get; set; }
        public int CostDiamonds { get; set; }
        public int CostCredits { get; set; }
    }

    private sealed class TaskRow
    {
        public string TrackId { get; set; } = "";
        public string Id { get; set; } = "";
        public string ActionType { get; set; } = "";
        public string? Parameter { get; set; }
        public int Premium { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class LevelRow
    {
        public string TrackId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public int Level { get; set; }
        public int RequiredCount { get; set; }
        public int PointsReward { get; set; }
        public int Premium { get; set; }
    }

    private sealed class PrizeRow
    {
        public string TrackId { get; set; } = "";
        public string Id { get; set; } = "";
        public int RequiredPoints { get; set; }
        public int ProductItemTypeId { get; set; }
        public string RewardType { get; set; } = "";
        public string? ExtraParams { get; set; }
        public int RewardAmount { get; set; }
        public int Premium { get; set; }
        public int SortOrder { get; set; }
    }

    private sealed class UserTrackRow
    {
        public string TrackId { get; set; } = "";
        public int Points { get; set; }
        public int Premium { get; set; }
    }

    private sealed class UserTaskRow
    {
        public string TrackId { get; set; } = "";
        public string TaskId { get; set; } = "";
        public int ProgressCount { get; set; }
    }

    private sealed class UserPrizeRow
    {
        public string TrackId { get; set; } = "";
        public string PrizeId { get; set; } = "";
    }

    private sealed class WalletRow
    {
        public int Credits { get; set; }
        public int Diamonds { get; set; }
    }
}
