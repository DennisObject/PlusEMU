using Dapper;
using Plus.Core;
using System.Collections.Concurrent;
using System.Data;
using Plus.Communication.Packets.Outgoing.Inventory.Purse;
using Plus.Communication.Packets.Outgoing.Notifications;
using Plus.Database;
using Plus.HabboHotel.Badges;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Rewards;

public class RewardManager : IRewardManager, IStartable
{
    private readonly IDatabase _database;
    private readonly IBadgeManager _badgeManager;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<int, List<int>> _rewardLogs;
    private readonly ConcurrentDictionary<int, Reward> _rewards;

    public RewardManager(IDatabase database, IBadgeManager badgeManager, TimeProvider clock)
    {
        _database = database;
        _badgeManager = badgeManager;
        _clock = clock;
        _rewards = new();
        _rewardLogs = new();
    }

    public int StartOrder => 30;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var rewards = await connection.QueryAsync<RewardRow>("SELECT id, reward_start AS Start, reward_end AS End, reward_type AS Type, reward_data AS Data, message FROM server_rewards WHERE enabled = TRUE");
        var logs = await connection.QueryAsync<(int UserId, int RewardId)>("SELECT user_id, reward_id FROM server_reward_logs");
        _rewards.Clear();
        _rewardLogs.Clear();

        foreach (var reward in rewards) {
            _rewards.TryAdd(reward.Id, new(reward.Start, reward.End, reward.Type, reward.Data, reward.Message));
        }

        foreach (var log in logs) {
            var userLogs = _rewardLogs.GetOrAdd(log.UserId, _ => new());

            if (!userLogs.Contains(log.RewardId)) {
                userLogs.Add(log.RewardId);
            }
        }
    }

    private sealed class RewardRow
    {
        public int Id { get; set; }
        public DateTimeOffset? Start { get; set; }
        public DateTimeOffset? End { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Data { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }


    private bool HasReward(int id, int rewardId)
    {
        if (!_rewardLogs.ContainsKey(id)) {
            return false;
        }

        if (_rewardLogs[id].Contains(rewardId)) {
            return true;
        }

        return false;
    }

    private void LogReward(int id, int rewardId)
    {
        if (!_rewardLogs.ContainsKey(id)) {
            _rewardLogs.TryAdd(id, new());
        }

        if (!_rewardLogs[id].Contains(rewardId)) {
            _rewardLogs[id].Add(rewardId);
        }

        using var connection = _database.Connection();
        connection.Execute("INSERT INTO server_reward_logs (user_id, reward_id) VALUES (@userId, @rewardId)", new { userId = id, rewardId });
    }

    public async Task CheckRewards(GameClient session)
    {
        if (session == null || session.GetHabbo() == null) {
            return;
        }

        var now = _clock.GetUtcNow();

        foreach (var entry in _rewards) {
            var id = entry.Key;
            var reward = entry.Value;

            if (HasReward(session.GetHabbo().Id, id)) {
                continue;
            }

            if (reward.IsActiveAt(now)) {
                switch (reward.Type) {
                    case RewardType.Badge: {
                            // Without a loaded inventory the badge cannot be given, so the reward stays unclaimed rather than logged.
                            if (session.GetHabbo().Inventory is not { } inventory) {
                                continue;
                            }

                            if (!inventory.Badges.HasBadge(reward.RewardData)) {
                                await _badgeManager.GiveBadge(session.GetHabbo(), reward.RewardData);
                            }

                            break;
                        }
                    case RewardType.Credits: {
                            lock (session.GetHabbo().WalletSync) {
                                if (session.GetHabbo().WalletClosed) {
                                    break;
                                }

                                session.GetHabbo().Credits += Convert.ToInt32(reward.RewardData);
                                session.Send(new CreditBalanceComposer(session.GetHabbo().Credits));
                            }

                            break;
                        }
                    case RewardType.Duckets: {
                            lock (session.GetHabbo().WalletSync) {
                                if (session.GetHabbo().WalletClosed) {
                                    break;
                                }

                                session.GetHabbo().Duckets += Convert.ToInt32(reward.RewardData);
                                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Duckets, Convert.ToInt32(reward.RewardData)));
                            }

                            break;
                        }
                    case RewardType.Diamonds: {
                            lock (session.GetHabbo().WalletSync) {
                                if (session.GetHabbo().WalletClosed) {
                                    break;
                                }

                                session.GetHabbo().Diamonds += Convert.ToInt32(reward.RewardData);
                                session.Send(new HabboActivityPointNotificationComposer(session.GetHabbo().Diamonds, Convert.ToInt32(reward.RewardData), 5));
                            }

                            break;
                        }
                }

                if (!string.IsNullOrEmpty(reward.Message)) {
                    session.SendNotification(reward.Message);
                }

                LogReward(session.GetHabbo().Id, id);
            }
            else {
                continue;
            }
        }
    }
}
