using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users.Messenger;

public class LoadUserMessengerTask : IUserDataLoadingTask
{
    private readonly IMessengerDataLoader _messengerDataLoader;

    public LoadUserMessengerTask(IMessengerDataLoader messengerDataLoader)
    {
        _messengerDataLoader = messengerDataLoader;
    }

    public async Task Load(Habbo habbo)
    {
        habbo.Messenger = new(
            (await _messengerDataLoader.GetBuddiesForUser(habbo.Id)).ToDictionary(buddy => buddy.Id),
            (await _messengerDataLoader.GetRequestsForUser(habbo.Id)).ToDictionary(request => request.FromId),
            await _messengerDataLoader.GetOutstandingRequestsForUser(habbo.Id));
        habbo.Messenger.FriendLimit = () => Plus.HabboHotel.Subscriptions.ClubLimits.For(habbo.Access, "friends", PlusEnvironment.SettingsManager);
    }
}