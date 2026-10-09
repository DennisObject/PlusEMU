using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Communication.Packets.Outgoing.Moderation;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Users.Messenger;
using Plus.Utilities;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Friends;

public readonly record struct RoomInvitationRequest
{
    public RoomInvitationRequest(IEnumerable<int> recipientIds, string message)
    {
        RecipientIds = recipientIds.Take(100).ToImmutableArray();
        Message = message;
    }

    public ImmutableArray<int> RecipientIds { get; }
    public string Message { get; }
}

[Singleton]
public interface IMessengerSocialMutationService
{
    Task SetRelationship(GameClient session, int friendId, int relationship);
    Task SendRoomInvites(GameClient session, RoomInvitationRequest request);
}

public sealed class MessengerSocialMutationService(
    IMessengerDataLoader messengerData,
    IRoomInvitationStore invitations,
    IGameClientManager clients,
    IRewardTrackManager rewards,
    TimeProvider clock) : IMessengerSocialMutationService
{
    private readonly ConditionalWeakTable<HabboMessenger, SemaphoreSlim> _relationshipGates = new();

    public async Task SetRelationship(GameClient session, int friendId, int relationship)
    {
        var habbo = session.GetHabbo();
        var messenger = habbo.Messenger;
        var friend = messenger?.GetFriend(friendId);

        if (messenger == null || friend == null) {
            session.Send(new BroadcastMessageAlertComposer("Oops, you can only set a relationship where a friendship exists."));

            return;
        }

        if (relationship is < 0 or > 3) {
            session.Send(new BroadcastMessageAlertComposer("Oops, you've chosen an invalid relationship type."));

            return;
        }

        var gate = _relationshipGates.GetValue(messenger, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try {
            friend = messenger.GetFriend(friendId);

            if (friend == null) {
                return;
            }

            var previous = friend.Relationship;
            await messengerData.SetRelationship(habbo.Id, friend.Id, relationship);
            friend.Relationship = relationship;

            if (relationship is >= 1 and <= 3 && relationship != previous) {
                rewards.Progress(session, RewardTrackActions.SetRelationshipStatus);
            }

            messenger.UpdateFriend(friend);
        }
        finally {
            gate.Release();
        }
    }

    public async Task SendRoomInvites(GameClient session, RoomInvitationRequest request)
    {
        var habbo = session.GetHabbo();

        // Invites only go to friends; without a loaded messenger there is no one to invite or log.
        if (habbo.Messenger is not { } messenger) {
            return;
        }

        if (habbo.TimeMuted > 0) {
            session.SendNotification("Oops, you're currently muted - you cannot send room invitations.");

            return;
        }

        var message = StringCharFilter.Escape(request.Message);

        if (message.Length > 121) {
            message = message[..121];
        }

        var recipients = request.RecipientIds
            .Where(messenger.FriendshipExists)
            .Select(clients.GetClientByUserId)
            .Where(client => client?.GetHabbo() is { AllowMessengerInvites: false, AllowConsoleMessages: true })
            .Cast<GameClient>()
            .ToArray();

        var invitedAt = clock.GetUtcNow();
        await invitations.Log(habbo.Id, message, invitedAt);

        foreach (var recipient in recipients) {
            recipient.Send(new RoomInviteComposer(habbo.Id, message));
        }

        if (recipients.Length > 0) {
            rewards.Progress(session, RewardTrackActions.SendMessengerInvite);
        }
    }
}
