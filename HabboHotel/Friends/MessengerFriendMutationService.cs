using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

/// <summary>
/// Friend request and removal workflows. Storage commits first; in-memory state and presentation follow only after success.
/// </summary>
public interface IMessengerFriendMutationService
{
    Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId);
    Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId);
    Task DeclineAllRequestsAsync(Habbo habbo);
    Task<FriendRequestError?> SendRequestAsync(Habbo habbo, int toId);
    Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds);
}

public sealed class MessengerFriendMutationService(IMessengerDataLoader messengerData, IAccountSessionGate sessionGate, IGameClientManager clients) : IMessengerFriendMutationService
{
    public async Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId)
    {
        if (!habbo.Messenger.Requests.ContainsKey(fromId)) return FriendRequestError.NoFriendRequest;
        using var accounts = await LockAccountsAsync(habbo.Id, fromId);
        var result = await messengerData.AcceptFriendRequest(habbo.Id, fromId);
        if (result.Error is { } error) return error;

        var friend = result.From!;
        var me = result.To!;
        habbo.Messenger.RemoveRequest(fromId);
        me.Habbo = habbo;
        var requester = clients.GetClientByUserId(fromId)?.GetHabbo();
        if (requester != null)
        {
            friend.Habbo = requester;
            requester.Messenger.AddFriend(me);
        }
        habbo.Messenger.AddFriend(friend);
        return null;
    }

    public async Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId)
    {
        if (!habbo.Messenger.Requests.ContainsKey(fromId)) return FriendRequestError.NoFriendRequest;
        if (await messengerData.DeleteFriendRequest(fromId, habbo.Id) != 1) return FriendRequestError.NoFriendRequest;
        habbo.Messenger.RemoveRequest(fromId);
        return null;
    }

    public async Task DeclineAllRequestsAsync(Habbo habbo)
    {
        foreach (var fromId in habbo.Messenger.Requests.Keys.ToArray())
            await DeclineRequestAsync(habbo, fromId);
    }

    public async Task<FriendRequestError?> SendRequestAsync(Habbo habbo, int toId)
    {
        if (habbo.Messenger.Requests.ContainsKey(toId)) return await AcceptRequestAsync(habbo, toId);
        if (habbo.Messenger.OutstandingFriendRequests.Contains(toId)) return FriendRequestError.AlreadyOutstandingFriendRequest;
        using var accounts = await LockAccountsAsync(habbo.Id, toId);
        if (!await messengerData.RegisterFriendRequest(habbo.Id, toId)) return FriendRequestError.AlreadyOutstandingFriendRequest;
        return habbo.Messenger.SendFriendRequest(toId);
    }

    public async Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds)
    {
        foreach (var friendId in friendIds.Distinct())
        {
            var friend = habbo.Messenger.GetFriend(friendId);
            if (friend == null) continue;
            using var accounts = await LockAccountsAsync(habbo.Id, friendId);
            if (await messengerData.DeleteFriendship(habbo.Id, friendId) == 0) continue;
            habbo.Messenger.RemoveFriend(friend);
            var other = clients.GetClientByUserId(friendId)?.GetHabbo().Messenger.GetFriend(habbo.Id);
            if (other != null) clients.GetClientByUserId(friendId)!.GetHabbo().Messenger.RemoveFriend(other);
        }
    }

    // Two accounts are always entered in ascending id order, so concurrent workflows cannot deadlock on each other.
    private async Task<AccountLocks> LockAccountsAsync(int first, int second)
    {
        var low = Math.Min(first, second);
        var high = Math.Max(first, second);
        var lowLock = await sessionGate.EnterAsync(low);
        if (low == high) return new AccountLocks(lowLock, null);
        return new AccountLocks(lowLock, await sessionGate.EnterAsync(high));
    }

    private sealed class AccountLocks(IDisposable low, IDisposable? high) : IDisposable
    {
        public void Dispose()
        {
            high?.Dispose();
            low.Dispose();
        }
    }
}
