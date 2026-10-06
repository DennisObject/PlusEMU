using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

/// <summary>Outcome of sending a request: an error, or whether it was accepted instead of sent.</summary>
public sealed record FriendRequestOutcome(FriendRequestError? Error, bool Accepted = false);

/// <summary>
/// Friend request and removal workflows. Each workflow holds the canonical stripes of both accounts, revalidates memory
/// under that hold, commits storage first, and updates memory and presentation only after success.
/// </summary>
public interface IMessengerFriendMutationService
{
    Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId);
    Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId);
    Task DeclineAllRequestsAsync(Habbo habbo);
    Task<FriendRequestOutcome> SendRequestAsync(Habbo habbo, int toId);
    Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds);
}

public sealed class MessengerFriendMutationService(IMessengerDataLoader messengerData, IAccountSessionGate sessionGate, IGameClientManager clients) : IMessengerFriendMutationService
{
    public async Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId)
    {
        if (fromId == habbo.Id || !habbo.Messenger.Requests.ContainsKey(fromId))
        {
            return FriendRequestError.NoFriendRequest;
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, fromId]);

        return await AcceptLockedAsync(habbo, fromId);
    }

    public async Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId)
    {
        if (fromId == habbo.Id || !habbo.Messenger.Requests.ContainsKey(fromId))
        {
            return FriendRequestError.NoFriendRequest;
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, fromId]);

        if (!habbo.Messenger.Requests.ContainsKey(fromId))
        {
            return FriendRequestError.NoFriendRequest;
        }

        if (await messengerData.DeleteFriendRequest(fromId, habbo.Id) == 0)
        {
            return FriendRequestError.NoFriendRequest;
        }

        habbo.Messenger.RemoveRequest(fromId);

        return null;
    }

    public async Task DeclineAllRequestsAsync(Habbo habbo)
    {
        foreach (var fromId in habbo.Messenger.Requests.Keys.ToArray())
        {
            await DeclineRequestAsync(habbo, fromId);
        }
    }

    public async Task<FriendRequestOutcome> SendRequestAsync(Habbo habbo, int toId)
    {
        if (toId == habbo.Id)
        {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        if (habbo.Messenger.Requests.ContainsKey(toId))
        {
            return new(await AcceptRequestAsync(habbo, toId), Accepted: true);
        }

        if (habbo.Messenger.OutstandingFriendRequests.Contains(toId))
        {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, toId]);

        // Revalidate under the hold: an incoming request may have arrived after the checks above.
        if (habbo.Messenger.Requests.ContainsKey(toId))
        {
            return new(await AcceptLockedAsync(habbo, toId), Accepted: true);
        }

        if (habbo.Messenger.OutstandingFriendRequests.Contains(toId))
        {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        if (!await messengerData.RegisterFriendRequest(habbo.Id, toId))
        {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        habbo.Messenger.RecordOutgoingFriendRequest(toId);

        return new(null);
    }

    public async Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds)
    {
        foreach (var friendId in friendIds.Distinct())
        {
            if (friendId == habbo.Id || habbo.Messenger.GetFriend(friendId) == null)
            {
                continue;
            }

            using var accounts = await sessionGate.EnterManyAsync([habbo.Id, friendId]);
            // Everything the publication needs is read once, under the hold.
            var friend = habbo.Messenger.GetFriend(friendId);
            var other = clients.GetClientByUserId(friendId)?.GetHabbo();

            if (friend == null || await messengerData.DeleteFriendship(habbo.Id, friendId) == 0)
            {
                continue;
            }

            habbo.Messenger.RemoveFriend(friend);
            var otherBuddy = other?.Messenger.GetFriend(habbo.Id);

            if (other != null && otherBuddy != null)
            {
                other.Messenger.RemoveFriend(otherBuddy);
            }
        }
    }

    // Caller holds both accounts. Memory is read after the lock and changed only after the store commits.
    private async Task<FriendRequestError?> AcceptLockedAsync(Habbo habbo, int fromId)
    {
        if (!habbo.Messenger.Requests.ContainsKey(fromId))
        {
            return FriendRequestError.NoFriendRequest;
        }

        var result = await messengerData.AcceptFriendRequest(habbo.Id, fromId);

        if (result.Error is { } error)
        {
            return error;
        }

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
}
