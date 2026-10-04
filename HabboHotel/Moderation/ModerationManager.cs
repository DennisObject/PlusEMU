using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities;

namespace Plus.HabboHotel.Moderation;

public sealed class ModerationManager : IModerationManager
{
    private readonly IDatabase _database;
    private readonly ILogger<ModerationManager> _logger;
    private readonly ISessionIssuer _sessions;
    private readonly IGameClientManager _clients;
    private readonly IAccountSessionGate _sessionGate;
    private readonly ConcurrentDictionary<string, ModerationBan> _bans = new();
    private readonly Dictionary<int, List<ModerationPresetActions>> _moderationCfhTopicActions = new();


    private readonly Dictionary<int, string> _moderationCfhTopics = new();
    private readonly ConcurrentDictionary<int, ModerationTicket> _modTickets = new();
    private readonly List<string> _roomPresets = new();
    private readonly Dictionary<int, string> _userActionPresetCategories = new();
    private readonly Dictionary<int, List<ModerationPresetActionMessages>> _userActionPresetMessages = new();
    private readonly List<string> _userPresets = new();

    private int _ticketCount = 1;

    public ICollection<string> UserMessagePresets => _userPresets;

    public ICollection<string> RoomMessagePresets => _roomPresets;

    public ICollection<ModerationTicket> GetTickets => _modTickets.Values;

    public ModerationManager(IDatabase database, ILogger<ModerationManager> logger, ISessionIssuer sessions, IGameClientManager clients, IAccountSessionGate sessionGate)
    {
        _sessionGate = sessionGate;
        _sessions = sessions;
        _clients = clients;
        _database = database;
        _logger = logger;
    }

    public Dictionary<string, List<ModerationPresetActions>> UserActionPresets
    {
        get
        {
            var result = new Dictionary<string, List<ModerationPresetActions>>();
            foreach (var category in _moderationCfhTopics.ToList())
            {
                result.Add(category.Value, new());
                if (_moderationCfhTopicActions.ContainsKey(category.Key))
                    foreach (var data in _moderationCfhTopicActions[category.Key])
                        result[category.Value].Add(data);
            }
            return result;
        }
    }

    public void Init()
    {
        if (_userPresets.Count > 0)
            _userPresets.Clear();
        if (_moderationCfhTopics.Count > 0)
            _moderationCfhTopics.Clear();
        if (_moderationCfhTopicActions.Count > 0)
            _moderationCfhTopicActions.Clear();
        if (_bans.Count > 0)
            _bans.Clear();
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable presetsTable = null;
            dbClient.SetQuery("SELECT * FROM `moderation_presets`;");
            presetsTable = dbClient.GetTable();
            if (presetsTable != null)
            {
                foreach (DataRow row in presetsTable.Rows)
                {
                    var type = Convert.ToString(row["type"]).ToLower();
                    switch (type)
                    {
                        case "user":
                            _userPresets.Add(Convert.ToString(row["message"]));
                            break;
                        case "room":
                            _roomPresets.Add(Convert.ToString(row["message"]));
                            break;
                    }
                }
            }
        }
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable moderationTopics = null;
            dbClient.SetQuery("SELECT * FROM `moderation_topics`;");
            moderationTopics = dbClient.GetTable();
            if (moderationTopics != null)
            {
                foreach (DataRow row in moderationTopics.Rows)
                {
                    if (!_moderationCfhTopics.ContainsKey(Convert.ToInt32(row["id"])))
                        _moderationCfhTopics.Add(Convert.ToInt32(row["id"]), Convert.ToString(row["caption"]));
                }
            }
        }
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable moderationTopicsActions = null;
            dbClient.SetQuery("SELECT * FROM `moderation_topic_actions`;");
            moderationTopicsActions = dbClient.GetTable();
            if (moderationTopicsActions != null)
            {
                foreach (DataRow row in moderationTopicsActions.Rows)
                {
                    var parentId = Convert.ToInt32(row["parent_id"]);
                    if (!_moderationCfhTopicActions.ContainsKey(parentId)) _moderationCfhTopicActions.Add(parentId, new());
                    _moderationCfhTopicActions[parentId].Add(new(Convert.ToInt32(row["id"]), Convert.ToInt32(row["parent_id"]), Convert.ToString(row["type"]),
                        Convert.ToString(row["caption"]), Convert.ToString(row["message_text"]),
                        Convert.ToInt32(row["mute_time"]), Convert.ToInt32(row["ban_time"]), Convert.ToInt32(row["ip_time"]), Convert.ToInt32(row["trade_lock_time"]),
                        Convert.ToString(row["default_sanction"])));
                }
            }
        }
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable presetsActionCats = null;
            dbClient.SetQuery("SELECT * FROM `moderation_preset_action_categories`;");
            presetsActionCats = dbClient.GetTable();
            if (presetsActionCats != null)
                foreach (DataRow row in presetsActionCats.Rows)
                    _userActionPresetCategories.Add(Convert.ToInt32(row["id"]), Convert.ToString(row["caption"]));
        }
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable presetsActionMessages = null;
            dbClient.SetQuery("SELECT * FROM `moderation_preset_action_messages`;");
            presetsActionMessages = dbClient.GetTable();
            if (presetsActionMessages != null)
            {
                foreach (DataRow row in presetsActionMessages.Rows)
                {
                    var parentId = Convert.ToInt32(row["parent_id"]);
                    if (!_userActionPresetMessages.ContainsKey(parentId)) _userActionPresetMessages.Add(parentId, new());
                    _userActionPresetMessages[parentId].Add(new(Convert.ToInt32(row["id"]), Convert.ToInt32(row["parent_id"]), Convert.ToString(row["caption"]),
                        Convert.ToString(row["message_text"]),
                        Convert.ToInt32(row["mute_hours"]), Convert.ToInt32(row["ban_hours"]), Convert.ToInt32(row["ip_ban_hours"]), Convert.ToInt32(row["trade_lock_days"]),
                        Convert.ToString(row["notice"])));
                }
            }
        }
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable getBans = null;
            dbClient.SetQuery("SELECT `bantype`,`value`,`reason`,`expire` FROM `bans` WHERE `bantype` = 'machine' OR `bantype` = 'user'");
            getBans = dbClient.GetTable();
            if (getBans != null)
            {
                foreach (DataRow dRow in getBans.Rows)
                {
                    var value = Convert.ToString(dRow["value"]);
                    var reason = Convert.ToString(dRow["reason"]);
                    var expires = (double)dRow["expire"];
                    var type = Convert.ToString(dRow["bantype"]);
                    var ban = new ModerationBan(BanTypeUtility.GetModerationBanType(type), value, reason, expires);
                    if (ban != null)
                    {
                        if (expires > PlusEnvironment.GetUnixTimestamp())
                        {
                            if (!_bans.ContainsKey(value))
                                _bans.TryAdd(value, ban);
                        }
                        else
                        {
                            dbClient.SetQuery($"DELETE FROM `bans` WHERE `bantype` = '{BanTypeUtility.FromModerationBanType(ban.Type)}' AND `value` = @Key LIMIT 1");
                            dbClient.AddParameter("Key", value);
                            dbClient.RunQuery();
                        }
                    }
                }
            }
        }
        _logger.LogInformation("Loaded " + (_userPresets.Count + _roomPresets.Count) + " moderation presets.");
        _logger.LogInformation("Loaded " + _userActionPresetCategories.Count + " moderation categories.");
        _logger.LogInformation("Loaded " + _userActionPresetMessages.Count + " moderation action preset messages.");
        _logger.LogInformation("Cached " + _bans.Count + " username and machine bans.");
    }

    public void ReCacheBans()
    {
        if (_bans.Count > 0)
            _bans.Clear();
        using (var dbClient = _database.GetQueryReactor())
        {
            DataTable getBans = null;
            dbClient.SetQuery("SELECT `bantype`,`value`,`reason`,`expire` FROM `bans` WHERE `bantype` = 'machine' OR `bantype` = 'user'");
            getBans = dbClient.GetTable();
            if (getBans != null)
            {
                foreach (DataRow dRow in getBans.Rows)
                {
                    var value = Convert.ToString(dRow["value"]);
                    var reason = Convert.ToString(dRow["reason"]);
                    var expires = (double)dRow["expire"];
                    var type = Convert.ToString(dRow["bantype"]);
                    var ban = new ModerationBan(BanTypeUtility.GetModerationBanType(type), value, reason, expires);
                    if (ban != null)
                    {
                        if (expires > PlusEnvironment.GetUnixTimestamp())
                        {
                            if (!_bans.ContainsKey(value))
                                _bans.TryAdd(value, ban);
                        }
                        else
                        {
                            dbClient.SetQuery($"DELETE FROM `bans` WHERE `bantype` = '{BanTypeUtility.FromModerationBanType(ban.Type)}' AND `value` = @Key LIMIT 1");
                            dbClient.AddParameter("Key", value);
                            dbClient.RunQuery();
                        }
                    }
                }
            }
        }
        _logger.LogInformation("Cached " + _bans.Count + " username and machine bans.");
    }

    /// <summary>
    /// How long a ban may hold its caller, across every step of a compound ban. Kept under the packet manager's 5 s limit
    /// so a ban never times out the moderator's own packet; what does not finish in time continues in the background.
    /// </summary>
    public static readonly TimeSpan BanBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2) };
    private static readonly TimeSpan RetryAttemptTimeout = TimeSpan.FromSeconds(30);

    // Ban work that outlived its caller, so an unban can cancel it.
    private readonly ConcurrentDictionary<BanWork, CancellationTokenSource> _pendingBans = new();

    /// <summary>One ban being enforced. Its row id, once written, is what a delayed sign-out re-checks.</summary>
    private sealed class BanWork(string mod, ModerationBanType type, string value, string reason, double expire)
    {
        public string Mod { get; } = mod;
        public ModerationBanType Type { get; } = type;
        public string Value { get; } = value;
        public string Reason { get; } = reason;
        public double Expire { get; } = expire;
        public long BanId { get; set; }
    }

    public Task BanUser(string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp, CancellationToken deadline = default) =>
        Ban(new(mod, type, banValue, reason, expireTimestamp), heldUserId: 0, deadline);

    public Task BanUserHoldingGate(int heldUserId, string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp) =>
        Ban(new(mod, type, banValue, reason, expireTimestamp), heldUserId, default);

    private async Task Ban(BanWork work, int heldUserId, CancellationToken deadline)
    {
        // Fail closed from memory before any I/O: the covered sessions close and logins not yet past their gate are stopped.
        foreach (var client in OnlineCovered(work.Type, work.Value))
            CloseSession(client.GetHabbo().Id);

        using var budget = deadline.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(deadline) : new CancellationTokenSource(BanBudget);
        try
        {
            await Enforce(work, heldUserId, budget.Token);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Ban of {Type} {Value} did not finish within its budget; continuing in the background", work.Type, work.Value);
            ContinueInBackground(work);
        }
    }

    /// <summary>Writes the ban once, then signs out every account it covers. Safe to run again after a partial attempt.</summary>
    private async Task Enforce(BanWork work, int heldUserId, CancellationToken cancellationToken)
    {
        if (work.BanId == 0)
            work.BanId = await WriteBan(work, cancellationToken);
        var accounts = await BannedAccounts(work.Type, work.Value, cancellationToken);
        foreach (var userId in accounts)
            CloseSession(userId);
        foreach (var userId in accounts)
            await SignOut(work, userId, userId == heldUserId, cancellationToken);
    }

    /// <summary>
    /// Under the account's session gate, so a login that is loading the account attaches first. If this exact ban is still
    /// in force, the credentials are revoked, the gate is stamped for logins already past their ticket, and any session
    /// that registered meanwhile closes; a ban lifted meanwhile leaves the newer login alone.
    /// </summary>
    private async Task SignOut(BanWork work, int userId, bool gateHeld, CancellationToken cancellationToken)
    {
        using var gate = gateHeld ? null : await _sessionGate.EnterAsync(userId, cancellationToken);
        if (!await BanInForce(work.BanId, cancellationToken))
            return;
        await _sessions.RevokeAll(userId, cancellationToken);
        CloseSession(userId);
    }

    private void CloseSession(int userId)
    {
        _sessionGate.Revoke(userId);
        _clients.GetClientByUserId(userId)?.Disconnect();
    }

    private void ContinueInBackground(BanWork work)
    {
        var cancellation = new CancellationTokenSource();
        _pendingBans[work] = cancellation;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var delay in RetryDelays)
                {
                    await Task.Delay(delay, cancellation.Token);
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
                        attempt.CancelAfter(RetryAttemptTimeout);
                        // The caller's gate is long released by now, so every account's gate is taken here.
                        await Enforce(work, heldUserId: 0, attempt.Token);
                        return;
                    }
                    catch (Exception e) when (!cancellation.IsCancellationRequested)
                    {
                        _logger.LogWarning(e, "Retrying ban of {Type} {Value} failed", work.Type, work.Value);
                    }
                }
                _logger.LogError("Gave up enforcing ban of {Type} {Value}", work.Type, work.Value);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Lifted meanwhile.
            }
            finally
            {
                _pendingBans.TryRemove(work, out _);
                cancellation.Dispose();
            }
        });
    }

    private async Task<long> WriteBan(BanWork work, CancellationToken cancellationToken)
    {
        var banType = work.Type == ModerationBanType.Ip ? "ip" : work.Type == ModerationBanType.Machine ? "machine" : "user";
        long banId;
        using (var connection = _database.Connection())
        {
            banId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "INSERT INTO `bans` (`bantype`, `value`, `reason`, `expire`, `added_by`, `added_date`) VALUES (@banType, @banValue, @reason, @expire, @mod, @addedDate); " +
                "SELECT LAST_INSERT_ID();",
                new { banType, banValue = work.Value, reason = work.Reason, expire = work.Expire, mod = work.Mod, addedDate = PlusEnvironment.GetUnixTimestamp().ToString(CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken));
        }
        // A re-ban must also refresh the cached expiry.
        if (work.Type == ModerationBanType.Machine || work.Type == ModerationBanType.Username)
            _bans[work.Value] = new(work.Type, work.Value, work.Reason, work.Expire);
        return banId;
    }

    /// <summary>
    /// Whether the ban row still exists and has not expired. Ban expiries are written on both the local clock
    /// (UnixTimestamp.GetNow) and UTC across the emulator, so the ban counts as in force by whichever clock is earlier.
    /// </summary>
    private async Task<bool> BanInForce(long banId, CancellationToken cancellationToken)
    {
        var now = Math.Min(UnixTimestamp.GetNow(), PlusEnvironment.GetUnixTimestamp());
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM `bans` WHERE `id` = @banId AND `expire` > @now",
            new { banId, now }, cancellationToken: cancellationToken)) > 0;
    }

    /// <summary>The sessions a ban covers that can be found without the database: by username or handshake machine id.</summary>
    private IEnumerable<GameClient> OnlineCovered(ModerationBanType type, string banValue)
    {
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        return type switch
        {
            ModerationBanType.Username => _clients.GetClientByUsername(banValue) is { } client && client.GetHabbo() != null ? [client] : [],
            ModerationBanType.Machine => _clients.GetClients.Where(client => client.MachineId == banValue && client.GetHabbo() != null).ToList(),
            _ => []
        };
#pragma warning restore CS0618
    }

    /// <summary>
    /// Accounts a ban covers. A machine ban reaches the sessions online with that handshake machine id. An IP ban reaches
    /// the accounts whose users.ip_last is that address: the auth API records it (through its trusted proxies) at login,
    /// resume and registration, and game sessions carry no client address of their own, so this is best effort and misses
    /// accounts that have since used the address without an HTTP login. New logins from a banned address are refused by
    /// BanLookup regardless.
    /// </summary>
    internal async Task<IReadOnlyList<int>> BannedAccounts(ModerationBanType type, string banValue, CancellationToken cancellationToken = default)
    {
        if (type == ModerationBanType.Machine)
            return OnlineCovered(type, banValue).Select(client => client.GetHabbo().Id).Distinct().ToList();
        using var connection = _database.Connection();
        return (await connection.QueryAsync<int>(new CommandDefinition(type == ModerationBanType.Ip
            ? "SELECT `id` FROM `users` WHERE `ip_last` = @banValue"
            : "SELECT `id` FROM `users` WHERE `username` = @banValue", new { banValue }, cancellationToken: cancellationToken))).ToList();
    }

    public bool UnbanUser(string username)
    {
        int removed;
        using (var connection = _database.Connection())
            removed = connection.Execute("DELETE FROM `bans` WHERE `bantype` = 'user' AND `value` = @username", new { username });
        RemoveBan(username);
        // Ban work still running in the background for this account stops; it would re-check the deleted row anyway.
        foreach (var (work, cancellation) in _pendingBans)
        {
            if (work.Type == ModerationBanType.Username && string.Equals(work.Value, username, StringComparison.OrdinalIgnoreCase))
                cancellation.Cancel();
        }
        return removed > 0;
    }

    public bool TryAddTicket(ModerationTicket ticket)
    {
        ticket.Id = _ticketCount++;
        return _modTickets.TryAdd(ticket.Id, ticket);
    }

    public bool TryGetTicket(int ticketId, out ModerationTicket ticket) => _modTickets.TryGetValue(ticketId, out ticket);

    public bool UserHasTickets(int userId) => _modTickets.Any(x => x.Value.Sender.Id == userId && x.Value.Answered == false);

    public ModerationTicket GetTicketBySenderId(int userId) => _modTickets.FirstOrDefault(x => x.Value.Sender.Id == userId).Value;

    /// <summary>
    /// Runs a quick check to see if a ban record is cached in the server.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="ban"></param>
    /// <returns></returns>
    public bool IsBanned(string key, out ModerationBan ban)
    {
        if (_bans.TryGetValue(key, out ban))
        {
            if (!ban.Expired)
                return true;

            //This ban has expired, let us quickly remove it here.
            using (var dbClient = _database.GetQueryReactor())
            {
                dbClient.SetQuery($"DELETE FROM `bans` WHERE `bantype` = '{BanTypeUtility.FromModerationBanType(ban.Type)}' AND `value` = @Key LIMIT 1");
                dbClient.AddParameter("Key", key);
                dbClient.RunQuery();
            }

            //And finally, let us remove the ban record from the cache.
            _bans.TryRemove(key, out _);
            return false;
        }
        return false;
    }

    /// <summary>
    /// Run a quick database check to see if this ban exists in the database.
    /// </summary>
    /// <param name="machineId">The value of the ban.</param>
    /// <returns></returns>
    public bool HasMachineBanCheck(string machineId)
    {
        ModerationBan machineBanRecord = null;
        if (IsBanned(machineId, out machineBanRecord))
        {
            DataRow banRow = null;
            using var dbClient = _database.GetQueryReactor();
            dbClient.SetQuery("SELECT * FROM `bans` WHERE `bantype` = 'machine' AND `value` = @value LIMIT 1");
            dbClient.AddParameter("value", machineId);
            banRow = dbClient.GetRow();

            //If there is no more ban record, then we can simply remove it from our cache!
            if (banRow == null)
            {
                RemoveBan(machineId);
                return false;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Run a quick database check to see if this ban exists in the database.
    /// </summary>
    /// <param name="username">The value of the ban.</param>
    /// <returns></returns>
    public bool UsernameBanCheck(string username)
    {
        ModerationBan usernameBanRecord = null;
        if (IsBanned(username, out usernameBanRecord))
        {
            DataRow banRow = null;
            using var dbClient = _database.GetQueryReactor();
            dbClient.SetQuery("SELECT * FROM `bans` WHERE `bantype` = 'user' AND `value` = @value LIMIT 1");
            dbClient.AddParameter("value", username);
            banRow = dbClient.GetRow();

            //If there is no more ban record, then we can simply remove it from our cache!
            if (banRow == null)
            {
                RemoveBan(username);
                return false;
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove a ban from the cache based on a given value.
    /// </summary>
    /// <param name="value"></param>
    public void RemoveBan(string value)
    {
        _bans.TryRemove(value, out _);
    }
}