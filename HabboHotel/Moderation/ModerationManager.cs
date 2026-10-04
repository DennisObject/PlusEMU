using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Dapper;
using Plus.Database;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users.Authentication;

namespace Plus.HabboHotel.Moderation;

public sealed class ModerationManager : IModerationManager
{
    private readonly IDatabase _database;
    private readonly ILogger<ModerationManager> _logger;
    private readonly ISessionIssuer _sessions;
    private readonly IGameClientManager _clients;
    private readonly Dictionary<string, ModerationBan> _bans = new();
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

    public ModerationManager(IDatabase database, ILogger<ModerationManager> logger, ISessionIssuer sessions, IGameClientManager clients)
    {
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
                                _bans.Add(value, ban);
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
                                _bans.Add(value, ban);
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

    public void BanUser(string mod, ModerationBanType type, string banValue, string reason, double expireTimestamp)
    {
        var banType = type == ModerationBanType.Ip ? "ip" : type == ModerationBanType.Machine ? "machine" : "user";
        using (var dbClient = _database.GetQueryReactor())
        {
            dbClient.SetQuery(
                "REPLACE INTO `bans` (`bantype`, `value`, `reason`, `expire`, `added_by`,`added_date`) VALUES (@banType, @banValue, @reason, @expire, @mod, @addedDate);");
            dbClient.AddParameter("banType", banType);
            dbClient.AddParameter("banValue", banValue);
            dbClient.AddParameter("reason", reason);
            dbClient.AddParameter("expire", expireTimestamp);
            dbClient.AddParameter("mod", mod);
            dbClient.AddParameter("addedDate", PlusEnvironment.GetUnixTimestamp().ToString(CultureInfo.InvariantCulture));
            dbClient.RunQuery();
        }
        // REPLACE keeps one row per value, so a re-ban must also refresh the cached expiry.
        if (type == ModerationBanType.Machine || type == ModerationBanType.Username)
            _bans[banValue] = new(type, banValue, reason, expireTimestamp);

        // Every ban path (mod tool, :ban, :ipban, :mip, word-filter bans, housekeeping) signs the banned accounts out
        // everywhere. The ban row is written first, so a login that misses the revocation still sees the ban.
        foreach (var userId in BannedAccounts(type, banValue))
            _sessions.RevokeAll(userId).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Accounts a ban covers. Game sessions carry no client address, so an IP ban reaches the accounts last seen
    /// at that address (users.ip_last); a machine ban reaches the sessions online with that machine id.
    /// </summary>
    internal IReadOnlyList<int> BannedAccounts(ModerationBanType type, string banValue)
    {
        if (type == ModerationBanType.Machine)
        {
#pragma warning disable CS0618 // The handshake's machine id only lives on the session.
            return _clients.GetClients.Where(client => client.MachineId == banValue && client.GetHabbo() != null)
                .Select(client => client.GetHabbo().Id).Distinct().ToList();
#pragma warning restore CS0618
        }
        using var connection = _database.Connection();
        return connection.Query<int>(type == ModerationBanType.Ip
            ? "SELECT `id` FROM `users` WHERE `ip_last` = @banValue"
            : "SELECT `id` FROM `users` WHERE `username` = @banValue", new { banValue }).ToList();
    }

    public bool UnbanUser(string username)
    {
        int removed;
        using (var connection = _database.Connection())
            removed = connection.Execute("DELETE FROM `bans` WHERE `bantype` = 'user' AND `value` = @username", new { username });
        RemoveBan(username);
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
            _bans.Remove(key);
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
        _bans.Remove(value);
    }
}