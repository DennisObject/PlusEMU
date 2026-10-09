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
    // Requests live in the loaded messenger; a user without one has none to accept, decline or send through.
    public async Task<FriendRequestError?> AcceptRequestAsync(Habbo habbo, int fromId)
    {
        if (habbo.Messenger is not { } messenger || fromId == habbo.Id || !messenger.Requests.ContainsKey(fromId)) {
            return FriendRequestError.NoFriendRequest;
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, fromId]);

        return await AcceptLockedAsync(habbo, messenger, fromId);
    }

    public async Task<FriendRequestError?> DeclineRequestAsync(Habbo habbo, int fromId)
    {
        if (habbo.Messenger is not { } messenger || fromId == habbo.Id || !messenger.Requests.ContainsKey(fromId)) {
            return FriendRequestError.NoFriendRequest;
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, fromId]);

        if (!messenger.Requests.ContainsKey(fromId)) {
            return FriendRequestError.NoFriendRequest;
        }

        if (await messengerData.DeleteFriendRequest(fromId, habbo.Id) == 0) {
            return FriendRequestError.NoFriendRequest;
        }

        messenger.RemoveRequest(fromId);

        return null;
    }

    public async Task DeclineAllRequestsAsync(Habbo habbo)
    {
        if (habbo.Messenger is not { } messenger) {
            return;
        }

        foreach (var fromId in messenger.Requests.Keys.ToArray()) {
            await DeclineRequestAsync(habbo, fromId);
        }
    }

    public async Task<FriendRequestOutcome> SendRequestAsync(Habbo habbo, int toId)
    {
        if (toId == habbo.Id) {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        // A request can only be recorded in a loaded messenger, so nothing is stored without one.
        if (habbo.Messenger is not { } messenger) {
            throw new InvalidOperationException("The user has no loaded messenger to send a friend request from.");
        }

        if (messenger.Requests.ContainsKey(toId)) {
            return new(await AcceptRequestAsync(habbo, toId), Accepted: true);
        }

        if (messenger.OutstandingFriendRequests.Contains(toId)) {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        using var accounts = await sessionGate.EnterManyAsync([habbo.Id, toId]);

        // Revalidate under the hold: an incoming request may have arrived after the checks above.
        if (messenger.Requests.ContainsKey(toId)) {
            return new(await AcceptLockedAsync(habbo, messenger, toId), Accepted: true);
        }

        if (messenger.OutstandingFriendRequests.Contains(toId)) {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        if (!await messengerData.RegisterFriendRequest(habbo.Id, toId)) {
            return new(FriendRequestError.AlreadyOutstandingFriendRequest);
        }

        messenger.RecordOutgoingFriendRequest(toId);

        return new(null);
    }

    public async Task RemoveFriendsAsync(Habbo habbo, IReadOnlyList<int> friendIds)
    {
        if (habbo.Messenger is not { } messenger) {
            return;
        }

        foreach (var friendId in friendIds.Distinct()) {
            if (friendId == habbo.Id || messenger.GetFriend(friendId) == null) {
                continue;
            }

            using var accounts = await sessionGate.EnterManyAsync([habbo.Id, friendId]);
            // Everything the publication needs is read once, under the hold.
            var friend = messenger.GetFriend(friendId);
            var other = clients.GetClientByUserId(friendId)?.GetHabbo();

            if (friend == null || await messengerData.DeleteFriendship(habbo.Id, friendId) == 0) {
                continue;
            }

            messenger.RemoveFriend(friend);

            // The other side only mirrors the committed removal if their messenger is loaded.
            if (other?.Messenger is { } otherMessenger && otherMessenger.GetFriend(habbo.Id) is { } otherBuddy) {
                otherMessenger.RemoveFriend(otherBuddy);
            }
        }
    }

    // Caller holds both accounts. Memory is read after the lock and changed only after the store commits.
    private async Task<FriendRequestError?> AcceptLockedAsync(Habbo habbo, HabboMessenger messenger, int fromId)
    {
        if (!messenger.Requests.ContainsKey(fromId)) {
            return FriendRequestError.NoFriendRequest;
        }

        var result = await messengerData.AcceptFriendRequest(habbo.Id, fromId);

        if (result.Error is { } error) {
            return error;
        }

        var friend = result.From!;
        var me = result.To!;
        messenger.RemoveRequest(fromId);
        me.Habbo = habbo;
        var requester = clients.GetClientByUserId(fromId)?.GetHabbo();

        if (requester != null) {
            friend.Habbo = requester;
            // The requester's side mirrors the committed friendship only if their messenger is loaded.
            requester.Messenger?.AddFriend(me);
        }

        messenger.AddFriend(friend);

        return null;
    }
}
