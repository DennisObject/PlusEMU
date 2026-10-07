using Plus.HabboHotel.GameClients;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Groups.Forums
{
    [Singleton]
    public interface IGroupForumService
    {
        void ShowForum(GameClient session, int groupId);
        void ShowForums(GameClient session, int kind, int start, int count);
        void ShowThreads(GameClient session, int groupId, int start, int count);
        void ShowThread(GameClient session, int groupId, int threadId);
        void ShowMessages(GameClient session, int groupId, int threadId, int start, int count);
        void Post(GameClient session, int groupId, int threadId, string title, string body);
        void UpdateSettings(GameClient session, int groupId, ForumPermissions permissions);
        void UpdateThread(GameClient session, int groupId, int threadId, bool pinned, bool locked);
        void ModerateThread(GameClient session, int groupId, int threadId, int state);
        void ModerateMessage(GameClient session, int groupId, int threadId, int messageId, int state);
        void MarkRead(GameClient session, IReadOnlyList<ForumReadMarker> markers);
        void ShowUnread(GameClient session);
    }
}
