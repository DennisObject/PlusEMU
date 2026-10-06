using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

/// <summary>Exact outcome of an accept: the consumed request's error, or both buddy views once committed.</summary>
public sealed record FriendAcceptResult(FriendRequestError? Error, MessengerBuddy? From = null, MessengerBuddy? To = null);

public interface IMessengerDataLoader
{
    Task<List<MessengerBuddy>> GetBuddiesForUser(int userId);
    Task<List<MessengerRequest>> GetRequestsForUser(int userId);
    Task<List<int>> GetOutstandingRequestsForUser(int userId);
    Task<FriendAcceptResult> AcceptFriendRequest(int acceptorId, int fromId);
    Task<MessengerBuddy> CreateBuddy(int userId);
    Task<MessengerBuddy?> GetBuddy(int userId, int friendId);
    void BroadcastStatusUpdate(Habbo habbo, MessengerEventTypes eventType, string value);
    Task LogPrivateMessage(int fromId, int toId, string message);
    Task LogPrivateOfflineMessage(int fromId, int toId, string message);
    Task<int> DeleteFriendship(int userOneId, int userTwoId);
    Task SetRelationship(int userOneId, int userTwoId, int relationship);
    Task<int> DeleteFriendRequest(int fromUserId, int toUserId);
    Task<bool> RegisterFriendRequest(int fromUserId, int toUserId);
    Task<(int userId, bool blockFriendRequests)> CanReceiveFriendRequests(string name);
    Task<Dictionary<int, List<(string Message, int SecondsAgo)>>> GetAndDeleteOfflineMessages(int userId);
    Task<int> GetFriendCount(int userId);
    Task<Dictionary<int, (MessengerBuddy buddy, int count)>> GetRelationshipsForUserAsync(int userId);
}
