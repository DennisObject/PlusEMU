using Plus.HabboHotel.Friends;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Quests;

namespace Plus.Communication.Packets.Incoming.FriendList;

internal class RequestFriendEvent : IPacketEvent
{
    private readonly IQuestManager _questManager;
    private readonly IMessengerDataLoader _messengerDataLoader;

    public RequestFriendEvent(IQuestManager questManager, IMessengerDataLoader messengerDataLoader)
    {
        _questManager = questManager;
        _messengerDataLoader = messengerDataLoader;
    }

    public async Task Parse(GameClient session, IIncomingPacket packet)
    {
        var (userId, blocked) = await _messengerDataLoader.CanReceiveFriendRequests(packet.ReadString());
        if (userId == 0 || blocked)
            return;

        var messenger = session.GetHabbo().Messenger;
        var accepting = messenger.Requests.ContainsKey(userId);
        if (messenger.SendFriendRequest(userId) == null && !accepting)
            RewardTrackManager.Current?.Progress(session, RewardTrackActions.RequestFriend);
        _questManager.ProgressUserQuest(session, QuestType.SocialFriend);
        return;
    }
}