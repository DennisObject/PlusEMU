using Dapper;
using Plus.Core;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Concurrent;
using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Plus.Database;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users;

namespace Plus.HabboHotel.Groups;

public class GroupManager : IGroupManager, IStartable
{
    private readonly ILogger<GroupManager> _logger;
    private readonly IDatabase _database;
    private readonly IGroupMembershipLoader _memberships;
    private readonly TimeProvider _clock;
    private readonly Dictionary<int, GroupColours> _backgroundColours;
    private readonly List<GroupColours> _baseColours;

    private readonly List<GroupBadgeParts> _bases;

    private readonly object _groupLoadingSync;
    private readonly ConcurrentDictionary<int, Group> _groups;
    private readonly Dictionary<int, GroupColours> _symbolColours;
    private readonly List<GroupBadgeParts> _symbols;

    public GroupManager(ILogger<GroupManager> logger, IDatabase database, IGroupMembershipLoader memberships,
        TimeProvider clock)
    {
        _logger = logger;
        _database = database;
        _memberships = memberships;
        _clock = clock;
        _groupLoadingSync = new();
        _groups = new();
        _bases = new();
        _symbols = new();
        _baseColours = new();
        _symbolColours = new();
        _backgroundColours = new();
    }


    public ICollection<GroupBadgeParts> BadgeBases => _bases;

    public ICollection<GroupBadgeParts> BadgeSymbols => _symbols;

    public ICollection<GroupColours> BadgeBaseColours => _baseColours;

    public ICollection<GroupColours> BadgeSymbolColours => _symbolColours.Values;

    public ICollection<GroupColours> BadgeBackColours => _backgroundColours.Values;

    public int StartOrder => 20;
    public Task Start() => Load();

    public void Init() => Load().GetAwaiter().GetResult();

    private async Task Load()
    {
        using var connection = _database.Connection();
        var items = await connection.QueryAsync<(int Id, string Type, string FirstValue, string SecondValue)>("SELECT id, type, firstvalue, secondvalue FROM groups_items WHERE enabled = TRUE");
        _bases.Clear();
        _symbols.Clear();
        _baseColours.Clear();
        _symbolColours.Clear();
        _backgroundColours.Clear();

        foreach (var item in items) {
            switch (item.Type) {
                case "base":
                    _bases.Add(new(item.Id, item.FirstValue, item.SecondValue));
                    break;
                case "symbol":
                    _symbols.Add(new(item.Id, item.FirstValue, item.SecondValue));
                    break;
                case "color":
                    _baseColours.Add(new(item.Id, item.FirstValue));
                    break;
                case "color2":
                    _symbolColours.Add(item.Id, new(item.Id, item.FirstValue));
                    break;
                case "color3":
                    _backgroundColours.Add(item.Id, new(item.Id, item.FirstValue));
                    break;
            }
        }
    }

    public bool TryGetGroup(int id, [NotNullWhen(true)] out Group? group)
    {
        group = null;

        if (_groups.ContainsKey(id)) {
            return _groups.TryGetValue(id, out group);
        }

        lock (_groupLoadingSync) {
            if (_groups.ContainsKey(id)) {
                return _groups.TryGetValue(id, out group);
            }

            using var connection = _database.Connection();
            var row = connection.QuerySingleOrDefault<GroupRow>("SELECT id,name,`desc` AS Description,badge,room_id AS RoomId,owner_id AS OwnerId,created AS CreatedAt,CAST(CAST(state AS CHAR) AS UNSIGNED) AS State,colour1,colour2,admindeco AS AdminDeco,forum_enabled AS ForumEnabled FROM `groups` WHERE id=@id LIMIT 1", new { id });

            if (row != null) {
                group = new(row.Id, row.Name, row.Description, row.Badge, row.RoomId, row.OwnerId,
                    row.CreatedAt, row.State, row.Colour1, row.Colour2, row.AdminDeco, row.ForumEnabled,
                    _memberships.Load(row.Id));
                _groups.TryAdd(group.Id, group);

                return true;
            }
        }

        return false;
    }

    public bool TryCreateGroup(Habbo player, string name, string description, uint roomId, string badge, int colour1, int colour2, [NotNullWhen(true)] out Group? @group)
    {
        group = null;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(badge)) {
            return false;
        }

        using var connection = _database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var ownsAvailableRoom = connection.QuerySingleOrDefault<int?>(
            "SELECT id FROM rooms WHERE id=@roomId AND owner=@ownerId AND group_id=0 FOR UPDATE",
            new { roomId, ownerId = player.Id }, transaction);

        if (ownsAvailableRoom == null) {
            transaction.Rollback();

            return false;
        }

        var createdAt = _clock.GetUtcNow();
        connection.Execute("INSERT INTO `groups` (`name`,`desc`,badge,owner_id,created,room_id,state,colour1,colour2,admindeco) VALUES (@name,@description,@badge,@ownerId,@createdAt,@roomId,'0',@colour1,@colour2,0)", new { name, description, badge, ownerId = player.Id, createdAt = createdAt.UtcDateTime, roomId, colour1, colour2 }, transaction);
        var id = connection.ExecuteScalar<int>("SELECT LAST_INSERT_ID()", transaction: transaction);
        connection.Execute("INSERT INTO group_memberships (user_id,group_id,`rank`) VALUES (@userId,@id,1)", new { userId = player.Id, id }, transaction);
        var updated = connection.Execute(
            "UPDATE rooms SET group_id=@id WHERE id=@roomId AND owner=@ownerId AND group_id=0 LIMIT 1",
            new { id, roomId, ownerId = player.Id }, transaction);

        if (updated != 1) {
            transaction.Rollback();

            return false;
        }

        connection.Execute("DELETE FROM room_rights WHERE room_id=@roomId", new { roomId }, transaction);
        transaction.Commit();
        group = new(id, name, description, badge, roomId, player.Id, createdAt, 0, colour1, colour2, 0,
            false, GroupMembershipSnapshot.ForOwner(player.Id));

        if (!_groups.TryAdd(group.Id, group)) {
            return false;
        }

        return true;
    }

    public string GetColourCode(int id, bool colourOne)
    {
        if (colourOne) {
            if (_symbolColours.ContainsKey(id)) {
                return _symbolColours[id].Colour;
            }
        }
        else {
            if (_backgroundColours.ContainsKey(id)) {
                return _backgroundColours[id].Colour;
            }
        }

        return "";
    }

    public void DeleteGroup(int id)
    {
        Group? group = null;

        if (_groups.ContainsKey(id)) {
            _groups.TryRemove(id, out group);
        }

        if (group != null) {
            group.Dispose();
        }
    }

    public List<Group> GetGroupsForUser(int userId)
    {
        var groups = new List<Group>();
        using var connection = _database.Connection();

        foreach (var id in connection.Query<int>("SELECT g.id FROM group_memberships AS m INNER JOIN `groups` AS g ON m.group_id=g.id WHERE m.user_id=@userId", new { userId })) {
            if (TryGetGroup(id, out var group)) {
                groups.Add(group);
            }
        }

        return groups;
    }

    public Dictionary<int, string> GetAllBadgesInRoom(Room room)
    {
        var badges = new Dictionary<int, string>();

        foreach (var groupIds in room.GetRoomUserManager().GetRoomUsers().Select(user => user.GetClient()?.GetHabbo().HabboStats.FavouriteGroupId ?? 0).Where(g => g > 0).Distinct()) {
            if (!TryGetGroup(groupIds, out var group)) {
                continue;
            }

            badges.Add(group.Id, group.Badge);
        }

        return badges;
    }
    private sealed class GroupRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Badge { get; set; } = string.Empty;
        public uint RoomId { get; set; }
        public int OwnerId { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public int State { get; set; }
        public int Colour1 { get; set; }
        public int Colour2 { get; set; }
        public int AdminDeco { get; set; }
        public bool ForumEnabled { get; set; }
    }
}
