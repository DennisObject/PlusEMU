using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.HabboHotel.Friends;

/// <summary>Delivers messenger output without packet or composer types; the communication layer renders it.</summary>
public interface IMessengerCommunicationOutput
{
    void InstantMessageError(GameClient session, MessengerMessageErrors error, int userId);
    void Notice(GameClient session, string text);
}

public interface IMessengerCommunicationService
{
    Task SendMessage(GameClient session, int friendId, string text);
    Task RequestFriend(GameClient session, string username);
}

public sealed class MessengerCommunicationService(
    IWordFilterManager wordFilter,
    IMessengerDataLoader messengerData,
    IQuestManager quests,
    IRewardTrackManager rewards,
    IMessengerCommunicationOutput output,
    IMessengerFriendMutationService friends) : IMessengerCommunicationService
{
    public Task SendMessage(GameClient session, int friendId, string text)
    {
        var habbo = session.GetHabbo();
        var friend = habbo.Messenger.GetFriend(friendId);
        if (friend == null)
            output.InstantMessageError(session, MessengerMessageErrors.NotFriends, friendId);
        var message = wordFilter.CheckMessage(text);
        if (string.IsNullOrWhiteSpace(message))
            return Task.CompletedTask;
        if (habbo.TimeMuted > 0)
        {
            output.Notice(session, "Oops, you're currently muted - you cannot send messages.");
            return Task.CompletedTask;
        }
        // Without a friend there is nothing to deliver to, so the messenger is never asked to send.
        if (friend == null)
            return Task.CompletedTask;

        var error = habbo.Messenger.SendMessage(friend, message);
        if (error == null)
            rewards.Progress(session, RewardTrackActions.SendMessengerMessage);
        if (error == MessageError.Flooding)
            output.Notice(session, "You cannot send a message, you have flooded the console.\n\nYou can send a message in 60 seconds.");
        return Task.CompletedTask;
    }

    public async Task RequestFriend(GameClient session, string username)
    {
        var (userId, blocked) = await messengerData.CanReceiveFriendRequests(username);
        if (userId == 0 || blocked)
            return;

        var habbo = session.GetHabbo();
        var accepting = habbo.Messenger.Requests.ContainsKey(userId);
        // The reward waits for the stored request; a refused or accepting attempt earns none.
        if (await friends.SendRequestAsync(habbo, userId) == null && !accepting)
            rewards.Progress(session, RewardTrackActions.RequestFriend);
        // The attempt counts toward the social quest even when the messenger refuses it.
        quests.ProgressUserQuest(session, QuestType.SocialFriend);
    }
}
