using System.Collections.Immutable;
using Plus.Communication.Packets.Outgoing.FriendList;
using Plus.Core.Settings;
using Plus.HabboHotel.Cache;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

public interface IMessengerPresentationService
{
    Task ShowFriendList(GameClient session);
    void ShowFriendRequests(GameClient session);
}

public sealed class MessengerPresentationService(IMessengerDataLoader messengerDataLoader, ICacheManager cacheManager, ISettingsManager settings) : IMessengerPresentationService
{
    private const int FriendPageSize = 500;

    public async Task ShowFriendList(GameClient session)
    {
        var habbo = session.GetHabbo();
        var friends = habbo.Messenger.Friends.Values.Select(MessengerBuddySnapshot.Capture).ToImmutableArray();
        session.Send(new MessengerInitComposer(ClubLimits.For(habbo.Access, "friends", settings)));
        if (friends.IsEmpty)
            session.Send(new BuddyListComposer(ImmutableArray<MessengerBuddySnapshot>.Empty, 1, 0));
        else
        {
            var pages = (friends.Length - 1) / FriendPageSize + 1;
            var page = 0;
            foreach (var batch in friends.Chunk(FriendPageSize))
                session.Send(new BuddyListComposer(ImmutableArray.Create(batch), pages, page++));
        }

        var messages = await messengerDataLoader.GetAndDeleteOfflineMessages(habbo.Id);
        foreach (var (userId, report) in messages)
            foreach (var (message, secondsAgo) in report)
                session.Send(new NewConsoleMessageComposer(userId, message, secondsAgo));
    }

    public void ShowFriendRequests(GameClient session)
    {
        var requests = session.GetHabbo().Messenger.Requests.Values
            .Select(request => new FriendRequestData(request.FromId, request.Username,
                cacheManager.GenerateUser(request.FromId)?.Look ?? string.Empty))
            .ToList();
        session.Send(new FriendRequestsComposer(requests));
    }
}
