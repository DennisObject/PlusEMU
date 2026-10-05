using System.Collections.Concurrent;

namespace Plus.HabboHotel.Users.Messenger;

public class HabboMessenger
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<int, MessengerBuddy> _friends;
    public IReadOnlyDictionary<int, MessengerBuddy> Friends => _friends;
    private readonly ConcurrentDictionary<int, MessengerRequest> _requests;
    public IReadOnlyDictionary<int, MessengerRequest> Requests => _requests;
    private readonly List<int> _outstandingFriendRequests;
    public IReadOnlyCollection<int> OutstandingFriendRequests => _outstandingFriendRequests;

    public event EventHandler<MessengerBuddyModifiedEventArgs>? FriendUpdated;
    public event EventHandler<MessengerBuddiesModifiedEventArgs>? FriendsUpdated;

    public event EventHandler<FriendRequestModifiedEventArgs>? FriendRequestUpdated;
    public event EventHandler<MessengerMessageEventArgs>? RoomInviteReceived;
    public event EventHandler<MessengerMessageEventArgs>? MessageSend;
    public event EventHandler<MessengerMessageEventArgs>? MessageReceived;
    public event EventHandler<FriendStatusUpdatedEventArgs>? FriendStatusUpdated;

    public event EventHandler? StatusUpdated;

    public HabboMessenger(Dictionary<int, MessengerBuddy> friends, Dictionary<int, MessengerRequest> requests, List<int> outstandingFriendRequests, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastMessage = _timeProvider.GetUtcNow().UtcDateTime;
        _requests = new(requests);
        _friends = new(friends);
        _outstandingFriendRequests = outstandingFriendRequests;
    }

    public FriendRequestError? AddFriendRequest(MessengerRequest request)
    {
        if (_requests.TryAdd(request.FromId, request))
            FriendRequestUpdated?.Invoke(this, new(FriendRequestModificationType.Received, request));
        return null;
    }

    public FriendRequestError? SendFriendRequest(int toId)
    {
        if (_outstandingFriendRequests.Contains(toId))
            return FriendRequestError.AlreadyOutstandingFriendRequest;
        if (_requests.ContainsKey(toId))
            return AcceptFriendRequest(toId);
        FriendRequestUpdated?.Invoke(this, new(FriendRequestModificationType.Sent, new() { ToId = toId }));
        return null;
    }

    public Func<int> FriendLimit { get; set; } = () => 300;

    public FriendRequestError? AcceptFriendRequest(int fromId)
    {
        if (_friends.Count >= FriendLimit()) return FriendRequestError.FriendLimitReached;
        if (!_requests.TryRemove(fromId, out var request))
            return FriendRequestError.NoFriendRequest;
        FriendRequestUpdated?.Invoke(this, new(FriendRequestModificationType.Accepted, request));
        return null;
    }

    public FriendRequestError? DeclineFriendRequest(int fromId)
    {
        if (!_requests.TryRemove(fromId, out var request))
            return FriendRequestError.NoFriendRequest;
        FriendRequestUpdated?.Invoke(this, new(FriendRequestModificationType.Declined, request));
        return null;
    }

    public void ReceiveRoomInvite(MessengerBuddy friend, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        RoomInviteReceived?.Invoke(this, new(friend, message));
    }

    private int _messengerSpamCount = 0;
    private DateTime? _messengerSpamTime = null;
    private DateTime _lastMessage;

    private bool IncrementFloodCounter()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        // A pause cannot bypass an active cooldown, even after the burst counter resets.
        if (_messengerSpamTime is { } cooldown)
        {
            if (now < cooldown) return true;
            _messengerSpamTime = null;
            _messengerSpamCount = 0;
        }
        var timeSinceLastMessage = now - _lastMessage;
        if (timeSinceLastMessage > TimeSpan.FromSeconds(20))
            _messengerSpamCount = 0;
        if (timeSinceLastMessage <= TimeSpan.FromSeconds(5))
            _messengerSpamCount++;
        if (_messengerSpamCount >= 12)
        {
            _messengerSpamTime = now.AddMinutes(1);
            _messengerSpamCount = 0;
            return true;
        }
        return false;
    }

    internal bool TrySendHabbicon()
    {
        if (IncrementFloodCounter()) return false;
        _lastMessage = _timeProvider.GetUtcNow().UtcDateTime;
        return true;
    }

    public MessageError? SendMessage(MessengerBuddy friend, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return MessageError.EmptyMessage;
        if (IncrementFloodCounter()) return MessageError.Flooding;
        _lastMessage = _timeProvider.GetUtcNow().UtcDateTime;
        MessageSend?.Invoke(this, new(friend, message));
        return null;
    }

    public void ReceiveMessage(MessengerBuddy friend, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        MessageReceived?.Invoke(this, new(friend, message));
    }

    public void AddFriend(MessengerBuddy friend)
    {
        _friends.TryAdd(friend.Id, friend);
        FriendUpdated?.Invoke(this, new(BuddyModificationType.Added, friend));
        _outstandingFriendRequests.Remove(friend.Id);
    }

    public void UpdateFriend(MessengerBuddy friend) => FriendUpdated?.Invoke(this, new(BuddyModificationType.Updated, friend));

    public void RemoveFriend(MessengerBuddy friend)
    {
        if (_friends.TryRemove(friend.Id, out _))
            FriendUpdated?.Invoke(this, new(BuddyModificationType.Removed, friend));
    }

    public void UpdateFriendStatus(MessengerBuddy friend, MessengerEventTypes eventType, string value) => FriendStatusUpdated?.Invoke(this, new(friend, eventType, value));

    public MessengerBuddy? GetFriend(int userId) => _friends.TryGetValue(userId, out var friend) ? friend : null;

    public bool FriendshipExists(int userId) => _friends.ContainsKey(userId);

    public void NotifyChangesToFriends() => StatusUpdated?.Invoke(this, EventArgs.Empty);

    public static Dictionary<int, (MessengerBuddy friend, int count)> GetRelationships(ConcurrentDictionary<int, MessengerBuddy> friends)
    {
        return friends.Values
            .Where(f => f.Relationship > 0)
            .GroupBy(f => f.Relationship)
            .ToDictionary(g => g.Key, g => (g.First(), g.Count()));
    }

}
