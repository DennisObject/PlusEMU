using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Data;
using Microsoft.Extensions.Logging;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities;

namespace Plus.HabboHotel.Moderation;

public sealed class ModerationManager : IModerationManager, IStartable
{
    private readonly IDatabase _database;
    private readonly ILogger<ModerationManager> _logger;
    private readonly ISessionIssuer _sessions;
    private readonly IGameClientManager _clients;
    private readonly IAccountSessionGate _sessionGate;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, ModerationBan> _bans = new();
    private readonly Dictionary<int, List<ModerationPresetActions>> _moderationCfhTopicActions = new();


    private readonly Dictionary<int, string> _moderationCfhTopics = new();
    private readonly ConcurrentDictionary<int, ModerationTicket> _modTickets = new();
    private readonly List<string> _roomPresets = new();
    private readonly Dictionary<int, string> _userActionPresetCategories = new();
    private readonly Dictionary<int, List<ModerationPresetActionMessages>> _userActionPresetMessages = new();
    private readonly List<string> _userPresets = new();

    private int _ticketCount;

    public ICollection<string> UserMessagePresets => _userPresets;

    public ICollection<string> RoomMessagePresets => _roomPresets;

    public ICollection<ModerationTicket> GetTickets => _modTickets.Values;

    public ModerationManager(IDatabase database, ILogger<ModerationManager> logger, ISessionIssuer sessions, IGameClientManager clients,
        IAccountSessionGate sessionGate, TimeProvider clock)
    {
        _sessionGate = sessionGate;
        _sessions = sessions;
        _clients = clients;
        _database = database;
        _logger = logger;
        _clock = clock;
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

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var presets = await connection.QueryAsync<(string Type, string Message)>("SELECT type, message FROM moderation_presets");
        var topics = await connection.QueryAsync<(int Id, string Caption)>("SELECT id, caption FROM moderation_topics");
        var actions = await connection.QueryAsync<PresetActionRow>("SELECT id, parent_id AS ParentId, type, caption, message_text AS MessageText, mute_time AS MuteTime, ban_time AS BanTime, ip_time AS IpTime, trade_lock_time AS TradeLockTime, default_sanction AS DefaultSanction FROM moderation_topic_actions");
        var categories = await connection.QueryAsync<(int Id, string Caption)>("SELECT id, caption FROM moderation_preset_action_categories");
        var messages = await connection.QueryAsync<PresetMessageRow>("SELECT id, parent_id AS ParentId, caption, message_text AS MessageText, mute_hours AS MuteTime, ban_hours AS BanTime, ip_ban_hours AS IpTime, trade_lock_days AS TradeLockTime, notice FROM moderation_preset_action_messages");
        _userPresets.Clear();
        _roomPresets.Clear();
        _moderationCfhTopics.Clear();
        _moderationCfhTopicActions.Clear();
        _userActionPresetCategories.Clear();
        _userActionPresetMessages.Clear();
        foreach (var preset in presets)
        {
            switch (preset.Type.ToLowerInvariant())
            {
                case "user": _userPresets.Add(preset.Message); break;
                case "room": _roomPresets.Add(preset.Message); break;
            }
        }
        foreach (var topic in topics)
            _moderationCfhTopics.TryAdd(topic.Id, topic.Caption);
        foreach (var action in actions)
        {
            if (!_moderationCfhTopicActions.TryGetValue(action.ParentId, out var list))
                _moderationCfhTopicActions.Add(action.ParentId, list = new());
            list.Add(new(action.Id, action.ParentId, action.Type, action.Caption, action.MessageText, action.MuteTime, action.BanTime, action.IpTime, action.TradeLockTime, action.DefaultSanction));
        }
        foreach (var category in categories)
            _userActionPresetCategories.Add(category.Id, category.Caption);
        foreach (var message in messages)
        {
            if (!_userActionPresetMessages.TryGetValue(message.ParentId, out var list))
                _userActionPresetMessages.Add(message.ParentId, list = new());
            list.Add(new(message.Id, message.ParentId, message.Caption, message.MessageText, message.MuteTime, message.BanTime, message.IpTime, message.TradeLockTime, message.Notice));
        }
        await LoadBans(connection);
        _logger.LogInformation("Loaded {Presets} moderation presets, {Categories} categories and {Messages} action preset groups. Cached {Bans} bans.", _userPresets.Count + _roomPresets.Count, _userActionPresetCategories.Count, _userActionPresetMessages.Count, _bans.Count);
    }

    private sealed class PresetActionRow
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public int MuteTime { get; set; }
        public int BanTime { get; set; }
        public int IpTime { get; set; }
        public int TradeLockTime { get; set; }
        public string DefaultSanction { get; set; } = string.Empty;
    }

    private sealed class PresetMessageRow
    {
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Caption { get; set; } = string.Empty;
        public string MessageText { get; set; } = string.Empty;
        public int MuteTime { get; set; }
        public int BanTime { get; set; }
        public int IpTime { get; set; }
        public int TradeLockTime { get; set; }
        public string Notice { get; set; } = string.Empty;
    }

    public void ReCacheBans()
    {
        using var connection = _database.Connection();
        LoadBans(connection).GetAwaiter().GetResult();
    }

    private async Task LoadBans(IDbConnection connection)
    {
        var bans = await connection.QueryAsync<BanRow>("SELECT bantype AS Type, value, reason, expire AS ExpiresAt FROM bans WHERE bantype IN ('machine', 'user')");
        var now = _clock.GetUtcNow();
        _bans.Clear();
        foreach (var ban in bans)
        {
            if (ban.ExpiresAt is { } expiresAt && expiresAt > now)
                _bans.TryAdd(ban.Value, new(BanTypeUtility.GetModerationBanType(ban.Type), ban.Value, ban.Reason, expiresAt));
            else
                await connection.ExecuteAsync("DELETE FROM bans WHERE bantype = @type AND value = @value LIMIT 1", new { type = ban.Type, value = ban.Value });
        }
    }

    private sealed record BanRow(string Type, string Value, string Reason, DateTimeOffset? ExpiresAt);

    /// <summary>
    /// How long a ban may hold its caller, across every step of a compound ban. Kept under the packet manager's 5 s limit
    /// so a ban never times out the moderator's own packet; what does not finish in time continues in the background.
    /// </summary>
    public static readonly TimeSpan BanBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan[] RetryDelays = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2) };
    private static readonly TimeSpan RetryAttemptTimeout = TimeSpan.FromSeconds(30);

    // Ban writes and unbans take this in turn, so an unban either comes before a ban's row or finds its pending work.
    private readonly SemaphoreSlim _banWrites = new(1, 1);
    // Ban work that has written its row (or is still resolving it in the background) and not finished signing out.
    private readonly ConcurrentDictionary<BanWork, byte> _pendingBans = new();

    /// <summary>Test seam: runs after a ban row is inserted and before the ban is published to the cache.</summary>
    internal Func<Task>? AfterBanInsert { get; set; }

    /// <summary>One ban being enforced, kept whole through retries.</summary>
    private sealed class BanWork(string mod, ModerationBanType type, string value, string reason, DateTimeOffset? expiresAt, string account)
    {
        // Never disposed: an unban may cancel it at any time, and it is collected with the work.
        private readonly CancellationTokenSource _cancellation = new();

        public string Mod { get; } = mod;
        public ModerationBanType Type { get; } = type;
        /// <summary>The banned value; empty for an address part until it is resolved from <see cref="AddressOf"/>.</summary>
        public string Value { get; set; } = value;
        public string Reason { get; } = reason;
        public DateTimeOffset? ExpiresAt { get; } = expiresAt?.ToUniversalTime();
        /// <summary>The banned account's username, so unbanning it also stops this work; empty for standalone bans.</summary>
        public string Account { get; } = account;
        /// <summary>For an address part: the account whose recorded address is banned.</summary>
        public int AddressOf { get; init; }
        /// <summary>The ban's row, which every delayed sign-out re-checks.</summary>
        public long BanId { get; set; }
        /// <summary>Covered accounts: captured from memory before the first disconnect, completed from the database once.</summary>
        public HashSet<int> Accounts { get; } = new();
        public bool AccountsComplete { get; set; }
        public CancellationToken Cancelled => _cancellation.Token;

        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Not disposed by design; tolerated so the remaining cancellations always run.
            }
        }
    }

    public Task BanUser(string mod, ModerationBanType type, string banValue, string reason, DateTimeOffset? expiresAt, CancellationToken deadline = default) =>
        WithDeadline(deadline, token => Ban(new(mod, type, banValue, reason, expiresAt, type == ModerationBanType.Username ? banValue : string.Empty), 0, token));

    public Task BanAccount(string mod, int userId, string username, string reason, DateTimeOffset? expiresAt, CancellationToken deadline = default,
        bool includeAddress = false, string? machineId = null, int heldUserId = 0) =>
        WithDeadline(deadline, async token =>
        {
            await CountBan(userId, token);
            await Ban(new(mod, ModerationBanType.Username, username, reason, expiresAt, username), heldUserId, token);
            // The address is resolved inside the ban work, so running out of time defers it instead of dropping it.
            if (includeAddress)
                await Ban(new(mod, ModerationBanType.Ip, string.Empty, reason, expiresAt, username) { AddressOf = userId }, heldUserId, token);
            if (!string.IsNullOrEmpty(machineId))
                await Ban(new(mod, ModerationBanType.Machine, machineId, reason, expiresAt, username), heldUserId, token);
        });

    /// <summary>Runs a ban action on the caller's deadline, or on a fresh <see cref="BanBudget"/> created before any I/O.</summary>
    private static async Task WithDeadline(CancellationToken deadline, Func<CancellationToken, Task> action)
    {
        using var budget = deadline.CanBeCanceled ? null : new CancellationTokenSource(BanBudget);
        await action(budget?.Token ?? deadline);
    }

    private async Task CountBan(int userId, CancellationToken deadline)
    {
        try
        {
            using var connection = _database.Connection();
            await connection.ExecuteAsync(new CommandDefinition("UPDATE `user_info` SET `bans` = `bans` + 1 WHERE `user_id` = @userId LIMIT 1",
                new { userId }, cancellationToken: deadline));
        }
        catch (OperationCanceledException)
        {
            // Only the moderation counter; the ban itself goes ahead.
        }
    }

    private async Task Ban(BanWork work, int heldUserId, CancellationToken deadline)
    {
        // Fail closed from memory before any I/O, and remember who was covered: a session unregisters as it closes.
        foreach (var client in OnlineCovered(work.Type, work.Value).ToList())
        {
            var userId = client.GetHabbo().Id;
            work.Accounts.Add(userId);
            _sessionGate.Revoke(userId);
            client.Disconnect();
        }

        using var token = CancellationTokenSource.CreateLinkedTokenSource(deadline, work.Cancelled);
        try
        {
            await Enforce(work, heldUserId, token.Token);
            _pendingBans.TryRemove(work, out _);
        }
        catch (Exception) when (work.Cancelled.IsCancellationRequested)
        {
            _pendingBans.TryRemove(work, out _);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Ban of {Type} {Value} did not finish within its budget; continuing in the background", work.Type, work.Value);
            ContinueInBackground(work);
        }
    }

    /// <summary>Resolves and writes the ban once, then signs out every account it covers. Safe to run again after a partial attempt.</summary>
    private async Task Enforce(BanWork work, int heldUserId, CancellationToken cancellationToken)
    {
        if (work.Value.Length == 0 && work.AddressOf > 0)
        {
            work.Value = await AccountAddress(work.AddressOf, cancellationToken);
            if (work.Value.Length == 0)
                return; // No address on record for the account: nothing to ban.
        }
        if (work.BanId == 0)
            await WriteBan(work, cancellationToken);
        if (!work.AccountsComplete)
        {
            work.Accounts.UnionWith(await BannedAccounts(work.Type, work.Value, cancellationToken));
            work.AccountsComplete = true;
        }
        foreach (var userId in work.Accounts.ToList())
            await SignOut(work, userId, userId == heldUserId, cancellationToken);
    }

    /// <summary>
    /// Under the account's session gate, so a login that is loading the account attaches first. Nothing happens unless this
    /// exact ban is still in force; then the gate is stamped for logins already past their ticket, the credentials are
    /// revoked and any session that registered meanwhile closes. A ban lifted or expired meanwhile leaves newer logins alone.
    /// </summary>
    private async Task SignOut(BanWork work, int userId, bool gateHeld, CancellationToken cancellationToken)
    {
        using var gate = gateHeld ? null : await _sessionGate.EnterAsync(userId, cancellationToken);
        if (!await BanInForce(work.BanId, cancellationToken))
            return;
        _sessionGate.Revoke(userId);
        await _sessions.RevokeAll(userId, cancellationToken);
        _clients.GetClientByUserId(userId)?.Disconnect();
    }

    private void ContinueInBackground(BanWork work)
    {
        _pendingBans[work] = 0;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var delay in RetryDelays)
                {
                    await Task.Delay(delay, work.Cancelled);
                    try
                    {
                        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(work.Cancelled);
                        attempt.CancelAfter(RetryAttemptTimeout);
                        // The caller's gate is long released by now, so every account's gate is taken here.
                        await Enforce(work, heldUserId: 0, attempt.Token);
                        return;
                    }
                    catch (Exception e) when (!work.Cancelled.IsCancellationRequested)
                    {
                        _logger.LogWarning(e, "Retrying ban of {Type} {Value} failed", work.Type, work.Value);
                    }
                }
                _logger.LogError("Gave up enforcing ban of {Type} {Value}", work.Type, work.Value);
            }
            catch (OperationCanceledException) when (work.Cancelled.IsCancellationRequested)
            {
                // Lifted meanwhile.
            }
            finally
            {
                _pendingBans.TryRemove(work, out _);
            }
        });
    }

    /// <summary>Writes the ban row, registers its work and publishes it to the cache in one turn of <see cref="_banWrites"/>, which unbans also take.</summary>
    private async Task WriteBan(BanWork work, CancellationToken cancellationToken)
    {
        var banType = work.Type == ModerationBanType.Ip ? "ip" : work.Type == ModerationBanType.Machine ? "machine" : "user";
        await _banWrites.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var connection = _database.Connection())
            {
                var addedAt = _clock.GetUtcNow();
                work.BanId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                    "INSERT INTO `bans` (`bantype`, `value`, `reason`, `expire`, `added_by`, `added_date`) VALUES (@banType, @banValue, @reason, @expire, @mod, @addedDate); " +
                    "SELECT LAST_INSERT_ID();",
                    new { banType, banValue = work.Value, reason = work.Reason, expire = work.ExpiresAt?.UtcDateTime, mod = work.Mod, addedDate = addedAt.UtcDateTime },
                    cancellationToken: cancellationToken));
            }
            if (AfterBanInsert != null) await AfterBanInsert();
            _pendingBans[work] = 0;
            // Published in the same turn as the row: an unban, which clears the cache in its own turn, always comes
            // entirely before or after. A re-ban also refreshes the cached expiry.
            if (work.Type == ModerationBanType.Machine || work.Type == ModerationBanType.Username)
                _bans[work.Value] = new(work.Type, work.Value, work.Reason, work.ExpiresAt);
        }
        finally
        {
            _banWrites.Release();
        }
    }

    /// <summary>Whether the ban row still exists and has not expired.</summary>
    private async Task<bool> BanInForce(long banId, CancellationToken cancellationToken)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM `bans` WHERE `id` = @banId AND `expire` > @now",
            new { banId, now = _clock.GetUtcNow().UtcDateTime }, cancellationToken: cancellationToken)) > 0;
    }

    /// <summary>
    /// The address the server knows for an account: users.ip_last, recorded by the auth API through its trusted proxies.
    /// The game socket's own address is the proxy's, so it is never used.
    /// </summary>
    private async Task<string> AccountAddress(int userId, CancellationToken cancellationToken)
    {
        using var connection = _database.Connection();
        return await connection.ExecuteScalarAsync<string?>(new CommandDefinition("SELECT `ip_last` FROM `users` WHERE `id` = @userId",
            new { userId }, cancellationToken: cancellationToken)) ?? string.Empty;
    }

    /// <summary>The sessions a ban covers that can be found without the database: by username or handshake machine id.</summary>
    private IEnumerable<GameClient> OnlineCovered(ModerationBanType type, string banValue)
    {
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
        return type switch
        {
            ModerationBanType.Username => _clients.GetClientByUsername(banValue) is { } client && client.GetHabbo() != null ? [client] : [],
            ModerationBanType.Machine when banValue.Length > 0 => _clients.GetClients.Where(client => client.MachineId == banValue && client.GetHabbo() != null).ToList(),
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
        _banWrites.Wait();
        try
        {
            int removed;
            using (var connection = _database.Connection())
                removed = connection.Execute("DELETE FROM `bans` WHERE `bantype` = 'user' AND `value` = @username", new { username });
            RemoveBan(username);
            // Every part of a ban on this account still running stops; it would re-check its row anyway.
            foreach (var work in _pendingBans.Keys)
            {
                if (string.Equals(work.Account, username, StringComparison.OrdinalIgnoreCase))
                    work.Cancel();
            }
            return removed > 0;
        }
        finally
        {
            _banWrites.Release();
        }
    }

    public bool TryAddTicket(ModerationTicket ticket)
    {
        ticket.Id = Interlocked.Increment(ref _ticketCount);
        return _modTickets.TryAdd(ticket.Id, ticket);
    }

    public bool TryGetTicket(int ticketId, [NotNullWhen(true)] out ModerationTicket? ticket) => _modTickets.TryGetValue(ticketId, out ticket);

    public bool UserHasTickets(int userId) => _modTickets.Any(x => x.Value.Sender.Id == userId && x.Value.Answered == false);

    public ModerationTicket? GetTicketBySenderId(int userId) => _modTickets.Values.FirstOrDefault(ticket => ticket.Sender.Id == userId && !ticket.Answered);

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
            if (!ban.IsExpiredAt(_clock.GetUtcNow()))
                return true;

            //This ban has expired, let us quickly remove it here.
            using (var connection = _database.Connection())
                connection.Execute("DELETE FROM bans WHERE bantype = @type AND value = @key LIMIT 1", new { type = BanTypeUtility.FromModerationBanType(ban.Type), key });

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
        if (!IsBanned(machineId, out _))
            return false;
        using var connection = _database.Connection();
        var exists = connection.ExecuteScalar<bool>("SELECT EXISTS(SELECT 1 FROM bans WHERE bantype = @type AND value = @value)", new { type = "machine", value = machineId });
        if (!exists)
            RemoveBan(machineId);
        return exists;
    }

    /// <summary>
    /// Run a quick database check to see if this ban exists in the database.
    /// </summary>
    /// <param name="username">The value of the ban.</param>
    /// <returns></returns>
    public bool UsernameBanCheck(string username)
    {
        if (!IsBanned(username, out _))
            return false;
        using var connection = _database.Connection();
        var exists = connection.ExecuteScalar<bool>("SELECT EXISTS(SELECT 1 FROM bans WHERE bantype = @type AND value = @value)", new { type = "user", value = username });
        if (!exists)
            RemoveBan(username);
        return exists;
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
