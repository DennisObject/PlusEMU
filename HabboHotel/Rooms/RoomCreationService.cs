using Plus.Communication.Packets.Outgoing.Navigator;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Subscriptions;
using Plus.HabboHotel.Users.Authentication;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Rooms;

public readonly record struct RoomCreationRequest(
    string Name,
    string Description,
    string ModelName,
    int Category,
    int MaxVisitors,
    int TradeSettings);

[Singleton]
public interface IRoomCreationService
{
    Task SendCreationAvailability(GameClient session);
    Task Create(GameClient session, RoomCreationRequest request);
}

public sealed class RoomCreationService(
    IRoomDataLoader roomData,
    IRoomManager rooms,
    ISettingsManager settings,
    INavigatorManager navigator,
    IWordFilterManager wordFilter,
    IRewardTrackManager rewards,
    IAccountSessionGate accounts) : IRoomCreationService
{
    public async Task SendCreationAvailability(GameClient session)
    {
        var habbo = session.GetHabbo();
        using var account = await accounts.EnterAsync(habbo.Id);
        var limit = ClubLimits.For(habbo.Access, "rooms", settings);
        session.Send(new CanCreateRoomComposer(
            roomData.GetRoomsDataByOwnerSortByName(habbo.Id).Count >= limit,
            limit));
    }

    public async Task Create(GameClient session, RoomCreationRequest request)
    {
        var habbo = session.GetHabbo();
        using var account = await accounts.EnterAsync(habbo.Id);
        var name = wordFilter.CheckMessage(request.Name);
        var description = wordFilter.CheckMessage(request.Description);
        if (name.Length is < 3 or > 25 ||
            !rooms.TryGetModel(request.ModelName, out var model) ||
            !model.CanCreate(habbo.Access))
            return;

        var roomLimit = ClubLimits.For(habbo.Access, "rooms", settings);
        if (roomData.GetRoomsDataByOwnerSortByName(habbo.Id).Count >= roomLimit)
        {
            session.Send(new CanCreateRoomComposer(true, roomLimit));
            return;
        }

        navigator.TryGetSearchResultList(request.Category, out var category);
        var categoryId = RoomCategoryChoice.Resolve(
            request.Category, category, habbo.Access, habbo.Id, habbo.Id, false);
        var visitorLimit = ClubLimits.For(habbo.Access, "visitors", settings);
        var maxVisitors = request.MaxVisitors is < 10 || request.MaxVisitors > visitorLimit
            ? 10
            : request.MaxVisitors;
        var tradeSettings = request.TradeSettings is < 0 or > 2 ? 0 : request.TradeSettings;
        var created = rooms.CreateRoom(
            session, name, description, categoryId, maxVisitors, tradeSettings, model);
        if (created != null)
        {
            session.Send(new FlatCreatedComposer(created.Id, name));
            rewards.Progress(session, RewardTrackActions.CreateRoom);
        }
        habbo.Messenger.NotifyChangesToFriends();
    }
}
