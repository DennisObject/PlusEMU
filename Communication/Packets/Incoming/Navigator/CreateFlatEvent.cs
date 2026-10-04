using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;

namespace Plus.Communication.Packets.Incoming.Navigator;

internal class CreateFlatEvent : IPacketEvent
{
    private readonly IWordFilterManager _wordFilterManager;
    private readonly IRoomManager _roomManager;
    private readonly INavigatorManager _navigatorManager;
    private readonly Plus.Core.Settings.ISettingsManager _settings;

    public CreateFlatEvent(IWordFilterManager wordFilterManager, IRoomManager roomManager, INavigatorManager navigatorManager, Plus.Core.Settings.ISettingsManager settings)
    {
        _wordFilterManager = wordFilterManager;
        _roomManager = roomManager;
        _navigatorManager = navigatorManager;
        _settings = settings;
    }

    public Task Parse(GameClient session, IIncomingPacket packet)
    {
        var name = _wordFilterManager.CheckMessage(packet.ReadString());
        var description = _wordFilterManager.CheckMessage(packet.ReadString());
        var modelName = packet.ReadString();
        var category = packet.ReadInt();
        var maxVisitors = packet.ReadInt(); //10 = min, 25 = max.
        var tradeSettings = packet.ReadInt(); //2 = All can trade, 1 = owner only, 0 = no trading.
        if (name.Length < 3)
            return Task.CompletedTask;
        if (name.Length > 25)
            return Task.CompletedTask;
        if (!_roomManager.TryGetModel(modelName, out var model))
            return Task.CompletedTask;
        if (!model.CanCreate(session.GetHabbo().Access))
            return Task.CompletedTask;
        var rooms = RoomFactory.GetRoomsDataByOwnerSortByName(session.GetHabbo().Id);
        var limit = Plus.HabboHotel.Subscriptions.ClubLimits.For(session.GetHabbo().Access, "rooms", _settings);
        if (rooms.Count >= limit)
        {
            session.Send(new CanCreateRoomComposer(true, limit));
            return Task.CompletedTask;
        }
        if (!_navigatorManager.TryGetSearchResultList(category, out var searchResultList) ||
            searchResultList.CategoryType != NavigatorCategoryType.Category ||
            searchResultList.RequiredPermission.Length > 0 && !session.GetHabbo().Access.Can(searchResultList.RequiredPermission))
            category = 36;
        if (maxVisitors < 10 || maxVisitors > Plus.HabboHotel.Subscriptions.ClubLimits.For(session.GetHabbo().Access, "visitors", _settings))
            maxVisitors = 10;
        if (tradeSettings < 0 || tradeSettings > 2)
            tradeSettings = 0;
        var newRoom = _roomManager.CreateRoom(session, name, description, category, maxVisitors, tradeSettings, model);
        if (newRoom != null)
        {
            session.Send(new FlatCreatedComposer(newRoom.Id, name));
            RewardTrackManager.Current?.Progress(session, RewardTrackActions.CreateRoom);
        }

        session.GetHabbo().Messenger.NotifyChangesToFriends();
        return Task.CompletedTask;
    }
}