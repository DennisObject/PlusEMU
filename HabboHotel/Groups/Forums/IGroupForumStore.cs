using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups.Forums
{
    [Singleton]
    public interface IGroupForumStore
    {
        GroupForumSnapshot? Forum(ForumViewer viewer, int groupId, DateTimeOffset now);
        GroupForumsPage List(ForumViewer viewer, int kind, int start, int count, DateTimeOffset now);
        ForumThreadsPage? Threads(ForumViewer viewer, int groupId, int start, int count, DateTimeOffset now);
        ForumThreadSnapshot? Thread(ForumViewer viewer, int groupId, int threadId, DateTimeOffset now);
        ForumMessagesPage? Messages(ForumViewer viewer, int groupId, int threadId, int start, int count, DateTimeOffset now);
        ForumPostResult? Post(ForumViewer viewer, int groupId, int threadId, string title, string body, DateTimeOffset now);
        GroupForumSnapshot? Settings(ForumViewer viewer, int groupId, ForumPermissions permissions, DateTimeOffset now);
        ForumThreadSnapshot? UpdateThread(ForumViewer viewer, int groupId, int threadId, bool pinned, bool locked, DateTimeOffset now);
        ForumThreadSnapshot? ModerateThread(ForumViewer viewer, int groupId, int threadId, int state, DateTimeOffset now);
        ForumMessageSnapshot? ModerateMessage(ForumViewer viewer, int groupId, int threadId, int messageId, int state, DateTimeOffset now);
        void MarkRead(ForumViewer viewer, IReadOnlyList<ForumReadMarker> markers);
        int Unread(ForumViewer viewer);
    }
}
