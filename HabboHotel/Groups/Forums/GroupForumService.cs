using System.Data.Common;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing.Groups.Forums;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.HabboHotel.Groups.Forums
{
    public sealed class GroupForumService(IGroupForumStore store, TimeProvider clock, IWordFilterManager filter, ILogger<GroupForumService> logger) : IGroupForumService
    {
        public void ShowForum(GameClient session, int groupId) => Execute(session, (viewer, now) =>
            store.Forum(viewer, groupId, now) is { } forum ? new ForumDataComposer(forum) : null);
        public void ShowForums(GameClient session, int kind, int start, int count) => Execute(session, (viewer, now) =>
            new ForumsListDataComposer(store.List(viewer, kind, start, count, now)));
        public void ShowThreads(GameClient session, int groupId, int start, int count) => Execute(session, (viewer, now) =>
            store.Threads(viewer, groupId, start, count, now) is { } page ? new ThreadsListDataComposer(page) : null);
        public void ShowThread(GameClient session, int groupId, int threadId) => Execute(session, (viewer, now) =>
            store.Thread(viewer, groupId, threadId, now) is { } thread ? new ThreadUpdatedComposer(groupId, thread) : null);
        public void ShowMessages(GameClient session, int groupId, int threadId, int start, int count) => Execute(session, (viewer, now) =>
            store.Messages(viewer, groupId, threadId, start, count, now) is { } page ? new ThreadDataComposer(page) : null);
        public void Post(GameClient session, int groupId, int threadId, string title, string body) => Execute(session, (viewer, now) => {
            title = title.Trim();
            body = body.Trim();
            if (body.Length is < 10 or > 4000 || threadId == 0 && title.Length is < 10 or > 120) {
                return null;
            }
            if (session.GetHabbo()?.Access.Can(PermissionKeys.ChatFilterBypass) != true) {
                title = filter.CheckMessage(title);
                body = filter.CheckMessage(body);
            }
            var result = store.Post(viewer, groupId, threadId, title, body, now);
            return result == null ? null : result.NewThread
                ? new ThreadCreatedComposer(groupId, result.Thread)
                : new ThreadReplyComposer(groupId, threadId, result.Message);
        });
        public void UpdateSettings(GameClient session, int groupId, ForumPermissions permissions) => Execute(session, (viewer, now) =>
            store.Settings(viewer, groupId, permissions, now) is { } forum ? new ForumDataComposer(forum) : null);
        public void UpdateThread(GameClient session, int groupId, int threadId, bool pinned, bool locked) => Execute(session, (viewer, now) =>
            store.UpdateThread(viewer, groupId, threadId, pinned, locked, now) is { } thread ? new ThreadUpdatedComposer(groupId, thread) : null);
        public void ModerateThread(GameClient session, int groupId, int threadId, int state) => Execute(session, (viewer, now) =>
            store.ModerateThread(viewer, groupId, threadId, state, now) is { } thread ? new ThreadUpdatedComposer(groupId, thread) : null);
        public void ModerateMessage(GameClient session, int groupId, int threadId, int messageId, int state) => Execute(session, (viewer, now) =>
            store.ModerateMessage(viewer, groupId, threadId, messageId, state, now) is { } message ? new PostUpdatedComposer(groupId, threadId, message) : null);
        public void MarkRead(GameClient session, IReadOnlyList<ForumReadMarker> markers) => Execute(session, (viewer, _) => {
            store.MarkRead(viewer, markers);
            return new ForumsUnreadCountComposer(store.Unread(viewer));
        });
        public void ShowUnread(GameClient session) => Execute(session, (viewer, _) => new ForumsUnreadCountComposer(store.Unread(viewer)));

        private void Execute(GameClient session, Func<ForumViewer, DateTimeOffset, IServerPacket?> operation)
        {
            var habbo = session.GetHabbo();
            if (habbo == null || habbo.Id <= 0 || habbo.AccessClosed || !ReferenceEquals(habbo.Client, session)) {
                return;
            }
            IServerPacket? response;
            try {
                var viewer = new ForumViewer(habbo.Id, habbo.Access.Can(PermissionKeys.ModerationTool));
                response = operation(viewer, clock.GetUtcNow());
            }
            catch (DbException exception) {
                logger.LogError(exception, "Unable to process group forum request for {UserId}", habbo.Id);
                return;
            }
            if (response != null && ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session) && !habbo.AccessClosed) {
                session.Send(response);
            }
        }
    }
}
