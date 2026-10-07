using System.Collections.Immutable;
using System.Data;
using Dapper;
using Plus.Database;

namespace Plus.HabboHotel.Groups.Forums;

public sealed class GroupForumStore(IDatabase database) : IGroupForumStore
{
    private const string Member = "EXISTS(SELECT 1 FROM group_memberships gm WHERE gm.group_id=g.id AND gm.user_id=@userId)";
    private const string Admin = "EXISTS(SELECT 1 FROM group_memberships gm WHERE gm.group_id=g.id AND gm.user_id=@userId AND gm.`rank`<>'0')";
    private const string Readable = "(@staff OR g.owner_id=@userId OR COALESCE(f.read_permission,0)=0 OR COALESCE(f.read_permission,0)=1 AND " + Member + " OR COALESCE(f.read_permission,0)=2 AND " + Admin + ")";
    private const string ForumColumns = "g.id,g.name,g.`desc` AS Description,g.badge,g.owner_id AS OwnerId," +
        "COALESCE(f.read_permission,0) AS ReadPermission,COALESCE(f.post_permission,0) AS PostPermission," +
        "COALESCE(f.thread_permission,0) AS ThreadPermission,COALESCE(f.moderate_permission,2) AS ModeratePermission," +
        "COALESCE(f.message_count,0) AS Messages,COALESCE(r.last_message_id,0) AS LastRead," +
        "(SELECT COUNT(*) FROM group_forum_threads t WHERE t.group_id=g.id) AS Threads," +
        "m.author_id AS LastAuthorId,COALESCE(u.username,'') AS LastAuthorName,m.created_at AS LastPostedAt," + Member + " AS Member," + Admin + " AS Admin";
    private const string ForumFrom = " FROM `groups` g LEFT JOIN group_forums f ON f.group_id=g.id " +
        "LEFT JOIN group_forum_read_markers r ON r.group_id=g.id AND r.user_id=@userId " +
        "LEFT JOIN group_forum_messages m ON m.group_id=g.id AND m.id=f.message_count LEFT JOIN users u ON u.id=m.author_id";
    private const string ThreadColumns = "t.id,t.author_id AS AuthorId,COALESCE(u.username,'') AS AuthorName,t.title,t.pinned,t.locked," +
        "t.created_at AS CreatedAt,t.message_count AS Messages,t.last_message_id AS LastMessageId,t.state,t.moderator_id AS ModeratorId," +
        "t.moderated_at AS ModeratedAt,COALESCE(a.username,'') AS ModeratorName,m.author_id AS LastAuthorId," +
        "COALESCE(l.username,'') AS LastAuthorName,m.created_at AS LastPostedAt," +
        "(SELECT COUNT(*) FROM group_forum_messages p WHERE p.thread_id=t.id AND p.id>@lastRead) AS Unread";
    private const string ThreadFrom = " FROM group_forum_threads t LEFT JOIN users u ON u.id=t.author_id " +
        "LEFT JOIN users a ON a.id=t.moderator_id LEFT JOIN group_forum_messages m ON m.group_id=t.group_id AND m.id=t.last_message_id " +
        "LEFT JOIN users l ON l.id=m.author_id";
    private const string MessageColumns = "m.id,m.message_index AS MessageIndex,m.author_id AS AuthorId,COALESCE(u.username,'') AS AuthorName," +
        "COALESCE(u.look,'') AS AuthorFigure,m.created_at AS CreatedAt,m.body,m.state,m.moderator_id AS ModeratorId," +
        "COALESCE(a.username,'') AS ModeratorName,m.moderated_at AS ModeratedAt," +
        "(SELECT COUNT(*) FROM group_forum_messages p WHERE p.author_id=m.author_id) AS AuthorPosts";
    private const string MessageFrom = " FROM group_forum_messages m LEFT JOIN users u ON u.id=m.author_id LEFT JOIN users a ON a.id=m.moderator_id";

    public GroupForumSnapshot? Forum(ForumViewer viewer, int groupId, DateTimeOffset now) => WithForum(viewer, groupId, false,
        (connection, transaction, forum) => Summary(forum, viewer, now));

    public GroupForumsPage List(ForumViewer viewer, int kind, int start, int count, DateTimeOffset now)
    {
        if (kind is < 0 or > 2 || start < 0 || count is < 1 or > 50) {
            return new(kind, 0, start, []);
        }
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var where = " WHERE g.forum_enabled=TRUE AND " + Readable + (kind == 2 ? " AND (g.owner_id=@userId OR " + Member + ")" : "");
        var args = new { userId = viewer.UserId, staff = viewer.Staff, start, count };
        var total = connection.ExecuteScalar<int>("SELECT COUNT(*)" + ForumFrom + where, args, transaction);
        var order = kind == 1 ? "COALESCE(f.message_count,0) DESC,m.created_at DESC,g.id" : "m.created_at DESC,g.id";
        var rows = connection.Query<ForumRow>("SELECT " + ForumColumns + ForumFrom + where + " ORDER BY " + order + " LIMIT @count OFFSET @start", args, transaction);
        return new(kind, total, start, rows.Select(row => Summary(row, viewer, now)).ToImmutableArray());
    }

    public ForumThreadsPage? Threads(ForumViewer viewer, int groupId, int start, int count, DateTimeOffset now) => WithForum(viewer, groupId, false,
        (connection, transaction, forum) => !Can(forum, viewer, forum.ReadPermission) || start < 0 || count is < 1 or > 50 ? null :
            new ForumThreadsPage(groupId, start, connection.Query<ThreadRow>("SELECT " + ThreadColumns + ThreadFrom +
                " WHERE t.group_id=@groupId ORDER BY t.pinned DESC,t.updated_at DESC,t.id DESC LIMIT @count OFFSET @start",
                new { groupId, lastRead = forum.LastRead, start, count }, transaction).Select(row => Thread(row, forum, viewer, now)).ToImmutableArray()));

    public ForumThreadSnapshot? Thread(ForumViewer viewer, int groupId, int threadId, DateTimeOffset now) => WithForum(viewer, groupId, false,
        (connection, transaction, forum) => Can(forum, viewer, forum.ReadPermission) && ReadThread(connection, transaction, forum, threadId) is { } row
            ? Thread(row, forum, viewer, now) : null);

    public ForumMessagesPage? Messages(ForumViewer viewer, int groupId, int threadId, int start, int count, DateTimeOffset now) => WithForum(viewer, groupId, false,
        (connection, transaction, forum) => {
            if (!Can(forum, viewer, forum.ReadPermission) || start < 0 || count is < 1 or > 50 || ReadThread(connection, transaction, forum, threadId) is not { } thread) {
                return null;
            }
            var rows = connection.Query<MessageRow>("SELECT " + MessageColumns + MessageFrom +
                " WHERE m.group_id=@groupId AND m.thread_id=@threadId ORDER BY m.message_index LIMIT @count OFFSET @start",
                new { groupId, threadId, start, count }, transaction);
            return new ForumMessagesPage(groupId, threadId, start, rows.Select(row => Message(row, thread, forum, viewer, now)).ToImmutableArray());
        });

    public ForumPostResult? Post(ForumViewer viewer, int groupId, int threadId, string title, string body, DateTimeOffset now) => WithForum(viewer, groupId, true,
        (connection, transaction, forum) => {
            var newThread = threadId == 0;
            if (threadId < 0 || body.Length is < 10 or > 4000 || newThread && title.Length is < 10 or > 120 ||
                !Can(forum, viewer, forum.ReadPermission) || !Can(forum, viewer, newThread ? forum.ThreadPermission : forum.PostPermission)) {
                return null;
            }
            var thread = newThread ? null : ReadThread(connection, transaction, forum, threadId);
            if (!newThread && (thread == null || !Visible(thread.State, forum, viewer) || thread.Locked && !Moderates(forum, viewer))) {
                return null;
            }
            connection.Execute("INSERT IGNORE INTO group_forum_post_limits(user_id,posted_at) VALUES(@userId,@before)",
                new { userId = viewer.UserId, before = now.AddSeconds(-30).UtcDateTime }, transaction);
            var posted = connection.QuerySingle<DateTimeOffset>("SELECT posted_at FROM group_forum_post_limits WHERE user_id=@userId FOR UPDATE", new { userId = viewer.UserId }, transaction);
            if (now < posted.AddSeconds(30) || forum.Messages == int.MaxValue || thread?.Messages == int.MaxValue) {
                return null;
            }
            if (newThread) {
                connection.Execute("INSERT INTO group_forum_threads(group_id,author_id,title,created_at,updated_at) VALUES(@groupId,@userId,@title,@now,@now)",
                    new { groupId, userId = viewer.UserId, title, now = now.UtcDateTime }, transaction);
                threadId = connection.ExecuteScalar<int>("SELECT LAST_INSERT_ID()", transaction: transaction);
            }
            var messageId = forum.Messages + 1;
            connection.Execute("INSERT INTO group_forum_messages(group_id,id,thread_id,message_index,author_id,body,created_at) VALUES(@groupId,@messageId,@threadId,@index,@userId,@body,@now)",
                new { groupId, messageId, threadId, index = thread?.Messages ?? 0, userId = viewer.UserId, body, now = now.UtcDateTime }, transaction);
            connection.Execute("UPDATE group_forums SET message_count=@messageId WHERE group_id=@groupId; " +
                "UPDATE group_forum_threads SET message_count=message_count+1,last_message_id=@messageId,updated_at=@now WHERE group_id=@groupId AND id=@threadId; " +
                "UPDATE group_forum_post_limits SET posted_at=@now WHERE user_id=@userId",
                new { groupId, threadId, messageId, userId = viewer.UserId, now = now.UtcDateTime }, transaction);
            thread = ReadThread(connection, transaction, forum, threadId)!;
            var message = ReadMessage(connection, transaction, groupId, threadId, messageId)!;
            return new ForumPostResult(Thread(thread, forum, viewer, now), Message(message, thread, forum, viewer, now), newThread);
        });

    public GroupForumSnapshot? Settings(ForumViewer viewer, int groupId, ForumPermissions permissions, DateTimeOffset now) => WithForum(viewer, groupId, true,
        (connection, transaction, forum) => {
            if (!permissions.Valid || forum.OwnerId != (uint)viewer.UserId && !viewer.Staff) {
                return null;
            }
            connection.Execute("UPDATE group_forums SET read_permission=@read,post_permission=@post,thread_permission=@start,moderate_permission=@moderate WHERE group_id=@groupId",
                new { groupId, permissions.Read, permissions.Post, permissions.Start, permissions.Moderate }, transaction);
            forum.ReadPermission = permissions.Read;
            forum.PostPermission = permissions.Post;
            forum.ThreadPermission = permissions.Start;
            forum.ModeratePermission = permissions.Moderate;
            return Summary(forum, viewer, now);
        });

    public ForumThreadSnapshot? UpdateThread(ForumViewer viewer, int groupId, int threadId, bool pinned, bool locked, DateTimeOffset now) => WithForum(viewer, groupId, true,
        (connection, transaction, forum) => {
            var row = ReadThread(connection, transaction, forum, threadId);
            if (row == null || !Can(forum, viewer, forum.ReadPermission) || !Moderates(forum, viewer) || !Visible(row.State, forum, viewer)) {
                return null;
            }
            connection.Execute("UPDATE group_forum_threads SET pinned=@pinned,locked=@locked WHERE group_id=@groupId AND id=@threadId", new { groupId, threadId, pinned, locked }, transaction);
            row.Pinned = pinned;
            row.Locked = locked;
            return Thread(row, forum, viewer, now);
        });

    public ForumThreadSnapshot? ModerateThread(ForumViewer viewer, int groupId, int threadId, int state, DateTimeOffset now) => WithForum(viewer, groupId, true,
        (connection, transaction, forum) => {
            var row = ReadThread(connection, transaction, forum, threadId);
            if (row == null || !Can(forum, viewer, forum.ReadPermission) || !ModerationAllowed(forum, viewer, row.State, state)) {
                return null;
            }
            connection.Execute("UPDATE group_forum_threads SET state=@state,moderator_id=@userId,moderated_at=@now WHERE group_id=@groupId AND id=@threadId",
                new { groupId, threadId, state, userId = viewer.UserId, now = now.UtcDateTime }, transaction);
            return Thread(ReadThread(connection, transaction, forum, threadId)!, forum, viewer, now);
        });

    public ForumMessageSnapshot? ModerateMessage(ForumViewer viewer, int groupId, int threadId, int messageId, int state, DateTimeOffset now) => WithForum(viewer, groupId, true,
        (connection, transaction, forum) => {
            var thread = ReadThread(connection, transaction, forum, threadId);
            var row = ReadMessage(connection, transaction, groupId, threadId, messageId);
            if (thread == null || row == null || !Can(forum, viewer, forum.ReadPermission) || !ModerationAllowed(forum, viewer, row.State, state) || !Visible(thread.State, forum, viewer)) {
                return null;
            }
            connection.Execute("UPDATE group_forum_messages SET state=@state,moderator_id=@userId,moderated_at=@now WHERE group_id=@groupId AND thread_id=@threadId AND id=@messageId",
                new { groupId, threadId, messageId, state, userId = viewer.UserId, now = now.UtcDateTime }, transaction);
            return Message(ReadMessage(connection, transaction, groupId, threadId, messageId)!, thread, forum, viewer, now);
        });

    public void MarkRead(ForumViewer viewer, IReadOnlyList<ForumReadMarker> markers)
    {
        if (markers.Count > 100) {
            return;
        }
        foreach (var marker in markers) {
            WithForum(viewer, marker.GroupId, true, (connection, transaction, forum) => {
                if (marker.MessageId < 0 || !Can(forum, viewer, forum.ReadPermission)) {
                    return null;
                }
                var messageId = Math.Min(marker.MessageId, forum.Messages);
                connection.Execute("INSERT INTO group_forum_read_markers(group_id,user_id,last_message_id) VALUES(@groupId,@userId,@messageId) " +
                    "ON DUPLICATE KEY UPDATE last_message_id=GREATEST(last_message_id,@messageId)",
                    new { groupId = marker.GroupId, userId = viewer.UserId, messageId }, transaction);
                return new object();
            });
        }
    }

    public int Unread(ForumViewer viewer)
    {
        using var connection = database.Connection();
        return connection.ExecuteScalar<int>("SELECT COUNT(*)" + ForumFrom + " WHERE g.forum_enabled=TRUE AND " + Readable +
            " AND (g.owner_id=@userId OR " + Member + ") AND COALESCE(f.message_count,0)>COALESCE(r.last_message_id,0)", new { userId = viewer.UserId, staff = viewer.Staff });
    }

    private T? WithForum<T>(ForumViewer viewer, int groupId, bool write, Func<IDbConnection, IDbTransaction, ForumRow, T?> operation) where T : class
    {
        if (groupId <= 0 || viewer.UserId <= 0) {
            return null;
        }
        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        var args = new { groupId, userId = viewer.UserId, staff = viewer.Staff };
        if (write) {
            if (connection.ExecuteScalar<uint?>("SELECT id FROM `groups` WHERE id=@groupId AND forum_enabled=TRUE FOR UPDATE", args, transaction) == null) {
                return null;
            }
            connection.Execute("INSERT IGNORE INTO group_forums(group_id) VALUES(@groupId)", args, transaction);
        }
        var forum = connection.QuerySingleOrDefault<ForumRow>("SELECT " + ForumColumns + ForumFrom +
            " WHERE g.id=@groupId AND g.forum_enabled=TRUE", args, transaction);
        if (forum == null) {
            return null;
        }
        if (write) {
            // Membership changes do not lock the group, so hold the exact viewer rows through commit.
            var ranks = connection.Query<string>("SELECT `rank` FROM group_memberships WHERE group_id=@groupId AND user_id=@userId FOR UPDATE", args, transaction).ToArray();
            forum.Member = ranks.Length != 0;
            forum.Admin = ranks.Any(rank => rank != "0");
        }
        var result = operation(connection, transaction, forum);
        if (write && result != null) {
            transaction.Commit();
        }
        return result;
    }

    private static ThreadRow? ReadThread(IDbConnection connection, IDbTransaction transaction, ForumRow forum, int threadId) =>
        connection.QuerySingleOrDefault<ThreadRow>("SELECT " + ThreadColumns + ThreadFrom + " WHERE t.group_id=@groupId AND t.id=@threadId",
            new { groupId = forum.Id, threadId, lastRead = forum.LastRead }, transaction);
    private static MessageRow? ReadMessage(IDbConnection connection, IDbTransaction transaction, int groupId, int threadId, int messageId) =>
        connection.QuerySingleOrDefault<MessageRow>("SELECT " + MessageColumns + MessageFrom + " WHERE m.group_id=@groupId AND m.thread_id=@threadId AND m.id=@messageId",
            new { groupId, threadId, messageId }, transaction);
    private static bool Can(ForumRow forum, ForumViewer viewer, int permission) => viewer.Staff || forum.OwnerId == (uint)viewer.UserId || (permission switch
    { 0 => true, 1 => forum.Member, 2 => forum.Admin, _ => false });
    private static string Error(ForumRow forum, ForumViewer viewer, int permission) => Can(forum, viewer, permission) ? "" : permission switch
    { 1 => "not_member", 2 => "not_admin", _ => "not_owner" };
    private static bool Moderates(ForumRow forum, ForumViewer viewer) => Can(forum, viewer, forum.ModeratePermission);
    private static bool Visible(byte state, ForumRow forum, ForumViewer viewer) => state < 10 || viewer.Staff || state == 10 && Moderates(forum, viewer);
    private static bool ModerationAllowed(ForumRow forum, ForumViewer viewer, byte previous, int next) => next is 1 or 10 or 20 && Moderates(forum, viewer)
        && (next != 20 && previous != 20 || viewer.Staff);
    private static int Age(DateTimeOffset? value, DateTimeOffset now) => value is { } instant ? (int)Math.Clamp((now - instant).TotalSeconds, 0, int.MaxValue) : 0;
    private static GroupForumSnapshot Summary(ForumRow row, ForumViewer viewer, DateTimeOffset now) => new(checked((int)row.Id), row.Name, row.Description, row.Badge,
        row.Threads, row.Messages, Math.Max(0, row.Messages - row.LastRead), row.Messages, row.LastAuthorId ?? 0, row.LastAuthorName, Age(row.LastPostedAt, now),
        new(row.ReadPermission, row.PostPermission, row.ThreadPermission, row.ModeratePermission), Error(row, viewer, row.ReadPermission),
        Error(row, viewer, row.PostPermission), Error(row, viewer, row.ThreadPermission), Error(row, viewer, row.ModeratePermission), row.OwnerId == (uint)viewer.UserId || viewer.Staff, viewer.Staff);
    private static ForumThreadSnapshot Thread(ThreadRow row, ForumRow forum, ForumViewer viewer, DateTimeOffset now) => new(row.Id, row.AuthorId, row.AuthorName,
        Visible(row.State, forum, viewer) ? row.Title : "", row.Pinned, row.Locked, Age(row.CreatedAt, now), row.Messages, row.Unread, row.LastMessageId,
        row.LastAuthorId ?? 0, row.LastAuthorName, Age(row.LastPostedAt, now), row.State, row.ModeratorId, row.ModeratorName, Age(row.ModeratedAt, now));
    private static ForumMessageSnapshot Message(MessageRow row, ThreadRow thread, ForumRow forum, ForumViewer viewer, DateTimeOffset now) => new(row.Id, row.MessageIndex,
        row.AuthorId, row.AuthorName, row.AuthorFigure, Age(row.CreatedAt, now), Visible(row.State, forum, viewer) && Visible(thread.State, forum, viewer) ? row.Body : "",
        row.State, row.ModeratorId, row.ModeratorName, Age(row.ModeratedAt, now), row.AuthorPosts);

    private sealed class ForumRow
    {
        public uint Id { get; set; }
        public uint OwnerId { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Badge { get; set; } = "";
        public int ReadPermission { get; set; }
        public int PostPermission { get; set; }
        public int ThreadPermission { get; set; }
        public int ModeratePermission { get; set; }
        public int Threads { get; set; }
        public int Messages { get; set; }
        public int LastRead { get; set; }
        public int? LastAuthorId { get; set; }
        public string LastAuthorName { get; set; } = "";
        public DateTimeOffset? LastPostedAt { get; set; }
        public bool Member { get; set; }
        public bool Admin { get; set; }
    }
    private sealed class ThreadRow
    {
        public int Id { get; set; }
        public int AuthorId { get; set; }
        public string AuthorName { get; set; } = "";
        public string Title { get; set; } = "";
        public bool Pinned { get; set; }
        public bool Locked { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }
        public int Messages { get; set; }
        public int Unread { get; set; }
        public int LastMessageId { get; set; }
        public int? LastAuthorId { get; set; }
        public string LastAuthorName { get; set; } = "";
        public DateTimeOffset? LastPostedAt { get; set; }
        public byte State { get; set; }
        public int ModeratorId { get; set; }
        public string ModeratorName { get; set; } = "";
        public DateTimeOffset? ModeratedAt { get; set; }
    }
    private sealed class MessageRow
    {
        public int Id { get; set; }
        public int MessageIndex { get; set; }
        public int AuthorId { get; set; }
        public string AuthorName { get; set; } = "";
        public string AuthorFigure { get; set; } = "";
        public string Body { get; set; } = "";
        public DateTimeOffset? CreatedAt { get; set; }
        public byte State { get; set; }
        public int ModeratorId { get; set; }
        public string ModeratorName { get; set; } = "";
        public DateTimeOffset? ModeratedAt { get; set; }
        public int AuthorPosts { get; set; }
    }
}
