using Plus.HabboHotel.Friends;
using Plus.HabboHotel.Users.UserData;

namespace Plus.HabboHotel.Users.Messenger;

public class LoadUserMessengerTask : IUserDataLoadingTask
{
    private readonly IMessengerDataLoader _messengerDataLoader;
    private readonly TimeProvider _clock;

    public LoadUserMessengerTask(IMessengerDataLoader messengerDataLoader, TimeProvider clock)
    {
        _messengerDataLoader = messengerDataLoader;
        _clock = clock;
    }

    public async Task Load(Habbo habbo)
    {
        habbo.Messenger = new(
            (await _messengerDataLoader.GetBuddiesForUser(habbo.Id)).ToDictionary(buddy => buddy.Id),
            (await _messengerDataLoader.GetRequestsForUser(habbo.Id)).ToDictionary(request => request.FromId),
            await _messengerDataLoader.GetOutstandingRequestsForUser(habbo.Id),
            _clock);
        habbo.Messenger.FriendLimit = () => Plus.HabboHotel.Subscriptions.ClubLimits.For(habbo.Access, "friends", PlusEnvironment.SettingsManager);
    }
}