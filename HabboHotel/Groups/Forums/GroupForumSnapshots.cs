using System.Collections.Immutable;

namespace Plus.HabboHotel.Groups.Forums;

public readonly record struct ForumViewer(int UserId, bool Staff);
public readonly record struct ForumReadMarker(int GroupId, int MessageId, bool Read);
public sealed record ForumPermissions(int Read, int Post, int Start, int Moderate)
{
    public bool Valid => Read is >= 0 and <= 3 && Post >= Read && Post <= 3 && Start >= Post && Start <= 3 && Moderate is 2 or 3;
}

public sealed record GroupForumSnapshot(int Id, string Name, string Description, string Badge, int Threads,
    int Messages, int Unread, int LastMessageId, int LastAuthorId, string LastAuthorName, int LastPostSecondsAgo,
    ForumPermissions Permissions, string ReadError, string PostError, string StartError, string ModerateError,
    bool ChangeSettings, bool Staff);
public sealed record ForumThreadSnapshot(int Id, int AuthorId, string AuthorName, string Title, bool Pinned,
    bool Locked, int CreatedSecondsAgo, int Messages, int Unread, int LastMessageId, int LastAuthorId,
    string LastAuthorName, int LastPostSecondsAgo, byte State, int ModeratorId, string ModeratorName, int ModeratedSecondsAgo);
public sealed record ForumMessageSnapshot(int Id, int Index, int AuthorId, string AuthorName, string AuthorFigure,
    int CreatedSecondsAgo, string Body, byte State, int ModeratorId, string ModeratorName, int ModeratedSecondsAgo, int AuthorPosts);
public sealed record GroupForumsPage(int Kind, int Total, int Start, ImmutableArray<GroupForumSnapshot> Forums);
public sealed record ForumThreadsPage(int GroupId, int Start, ImmutableArray<ForumThreadSnapshot> Threads);
public sealed record ForumMessagesPage(int GroupId, int ThreadId, int Start, ImmutableArray<ForumMessageSnapshot> Messages);
public sealed record ForumPostResult(ForumThreadSnapshot Thread, ForumMessageSnapshot Message, bool NewThread);
