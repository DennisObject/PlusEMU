using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;
using Plus.HabboHotel.GameClients;

namespace Plus.HabboHotel.Friends;

internal class MessengerEventSynchronizer : IAuthenticationTask
{
    private readonly IMessengerDataLoader _messengerDataLoader;
    private readonly IGameClientManager _gameClientManager;

    public MessengerEventSynchronizer(IMessengerDataLoader messengerDataLoader, IGameClientManager gameClientManager)
    {
        _messengerDataLoader = messengerDataLoader;
        _gameClientManager = gameClientManager;
    }

    public Task UserLoggedIn(Habbo habbo)
    {
        habbo.Messenger.FriendRequestUpdated += (_, args) => OnFriendRequestUpdated(habbo, args);
        habbo.Messenger.FriendStatusUpdated += (_, args) => OnFriendStatusUpdated(habbo, args);
        habbo.Messenger.FriendUpdated += (_, args) => OnFriendUpdated(habbo, args);
        habbo.Messenger.FriendsUpdated += (_, args) => OnFriendsUpdated(habbo, args);
        habbo.Messenger.MessageSend += async (_, args) => await OnMessageSend(habbo, args);
        habbo.Messenger.MessageReceived += (_, args) => OnMessageReceived(habbo, args);
        habbo.Messenger.RoomInviteReceived += (_, args) => OnRoomInviteReceived(habbo, args);
        habbo.Messenger.StatusUpdated += (_, _) => OnStatusUpdated(habbo);
        NotifyOnlineStatus(habbo);

        return Task.CompletedTask;
    }

    private void OnStatusUpdated(Habbo habbo)
    {
        foreach (var friend in habbo.Messenger.Friends.Values)
        {
            var friendHabbo = friend.Habbo;
            var me = friendHabbo?.Messenger.GetFriend(habbo.Id);

            if (me == null)
            {
                continue;
            }

            friendHabbo?.Messenger.UpdateFriend(me);
        }
    }

    public Task UserLoggedOut(Habbo habbo)
    {
        NotifyOfflineStatus(habbo);

        return Task.CompletedTask;
    }


    private void OnRoomInviteReceived(Habbo habbo, MessengerMessageEventArgs args) => habbo.Client.Send(new RoomInviteComposer(args.Friend.Id, args.Message));

    private void OnMessageReceived(Habbo habbo, MessengerMessageEventArgs args) => habbo.Client.Send(new NewConsoleMessageComposer(args.Friend.Id, args.Message));

    private async Task OnMessageSend(Habbo habbo, MessengerMessageEventArgs args)
    {
        if (habbo.TimeMuted > 0)
        {
            habbo.Client.Send(new InstantMessageErrorComposer(MessengerMessageErrors.YourMuted, args.Friend.Id));

            return;
        }

        await _messengerDataLoader.LogPrivateMessage(habbo.Id, args.Friend.Id, args.Message);
        var target = _gameClientManager.GetClientByUserId(args.Friend.Id);

        if (target == null)
        {
            await _messengerDataLoader.LogPrivateOfflineMessage(habbo.Id, args.Friend.Id, args.Message);

            return;
        }

        if (target.GetHabbo().TimeMuted > 0)
        {
            habbo.Client.Send(new InstantMessageErrorComposer(MessengerMessageErrors.FriendMuted, args.Friend.Id));

            return;
        }

        if (!target.GetHabbo().AllowConsoleMessages || target.GetHabbo().IgnoresComponent.IsIgnored(habbo.Id))
        {
            habbo.Client.Send(new InstantMessageErrorComposer(MessengerMessageErrors.FriendBusy, args.Friend.Id));

            return;
        }

        var messenger = target.GetHabbo().Messenger;
        var friend = messenger.GetFriend(habbo.Id);

        if (friend == null)
        {
            return;
        }

        messenger.ReceiveMessage(friend, args.Message);
    }

    private void OnFriendsUpdated(Habbo habbo, MessengerBuddiesModifiedEventArgs args)
    {
        habbo.Client.Send(new FriendListUpdateComposer(args.Changes.Select(change => MessengerBuddyModification.Capture(change.Key, change.Value)).ToList()));
    }

    private void OnFriendUpdated(Habbo habbo, MessengerBuddyModifiedEventArgs args)
    {
        habbo.Client.Send(new FriendListUpdateComposer([MessengerBuddyModification.Capture(args.Buddy, args.BuddyModificationType)]));
    }

    private void OnFriendStatusUpdated(Habbo habbo, FriendStatusUpdatedEventArgs args) => habbo.Client.Send(new FriendNotificationComposer(args.Friend.Id, args.EventType, args.Value));

    // Friend requests and removals are persisted by IMessengerFriendMutationService before memory changes; these handlers only present.
    private void OnFriendRequestUpdated(Habbo habbo, FriendRequestModifiedEventArgs args)
    {
        if (args.FriendRequestModificationType == FriendRequestModificationType.Received)
        {
            habbo.Client.Send(new NewBuddyRequestComposer(args.Request.FromId, args.Request.Username, args.Request.Figure));
        }
        else if (args.FriendRequestModificationType == FriendRequestModificationType.Sent)
        {
            var target = _gameClientManager.GetClientByUserId(args.Request.ToId);

            if (target != null)
            {
                args.Request.FromId = habbo.Id;
                args.Request.Username = habbo.Username;
                args.Request.Figure = habbo.Look;
                target.GetHabbo().Messenger.AddFriendRequest(args.Request);
            }
        }
    }

    private void NotifyOnlineStatus(Habbo habbo)
    {
        foreach (var friend in habbo.Messenger.Friends)
        {
            var friendHabbo = _gameClientManager.GetClientByUserId(friend.Key);

            if (friendHabbo == null)
            {
                continue;
            }

            friend.Value.Habbo = friendHabbo.GetHabbo();
            var me = friendHabbo.GetHabbo().Messenger.GetFriend(habbo.Id);

            if (me == null)
            {
                continue;
            }

            me.Habbo = habbo;
            friendHabbo.GetHabbo().Messenger.UpdateFriend(me);
        }
    }

    private void NotifyOfflineStatus(Habbo habbo)
    {
        foreach (var friend in habbo.Messenger.Friends)
        {
            var friendHabbo = _gameClientManager.GetClientByUserId(friend.Key);

            if (friendHabbo == null)
            {
                continue;
            }

            friend.Value.Habbo = null;
            var me = friendHabbo.GetHabbo().Messenger.GetFriend(habbo.Id);

            if (me == null)
            {
                continue;
            }

            me.Habbo = null;
            friendHabbo.GetHabbo().Messenger.UpdateFriend(me);
        }
    }
}
