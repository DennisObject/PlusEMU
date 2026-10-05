using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Navigator;
using Plus.Communication.Packets.Outgoing;
using Plus.Core.Settings;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Navigator;
using Plus.HabboHotel.Quests;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Authentication;
using Plus.HabboHotel.Users.Messenger;
using Xunit;

namespace Plus.Tests;

public sealed class RoomCreationServiceTests
{
    [Fact]
    public async Task HandlersDecodeCompleteRequestAndDelegate()
    {
        var service = new RecordingService();

        await new CanCreateRoomEvent(service).Parse(null!, HabbiconTestSupport.Incoming());
        await new CreateFlatEvent(service).Parse(null!, HabbiconTestSupport.Incoming(
            "Room", "Description", "model_a", 17, 24, 2));

        Assert.True(service.AvailabilityRequested);
        Assert.Equal(new("Room", "Description", "model_a", 17, 24, 2), service.Request);
    }

    [Fact]
    public async Task InvalidFilteredNameDeniesCreationWithoutPublication()
    {
        var context = Context(filteredName: "no");

        await context.Service.Create(context.Client, new("valid", "description", "model_a", 17, 20, 1));

        Assert.Empty(context.Rooms.Created);
        Assert.Empty(context.Sent);
        Assert.Empty(context.Events);
    }

    [Fact]
    public async Task RoomLimitDeniesCreationWithLimitPacketOnly()
    {
        var context = Context(existingRooms: 2);

        await context.Service.Create(context.Client, new("valid", "description", "model_a", 17, 20, 1));

        Assert.Empty(context.Rooms.Created);
        Assert.Equal(ServerPacketHeader.CanCreateRoomComposer, Assert.Single(context.Sent).Header);
        Assert.Empty(context.Events);
    }

    [Fact]
    public async Task CreationFailureStillPublishesMessengerStatusOnly()
    {
        var context = Context(createSucceeds: false);

        await context.Service.Create(context.Client, new("valid", "description", "model_a", 17, 20, 1));

        Assert.Single(context.Rooms.Created);
        Assert.Empty(context.Sent);
        Assert.Equal(new[] { "messenger:0" }, context.Events);
    }

    [Fact]
    public async Task SuccessfulCreationNormalizesFieldsAndPreservesPublicationOrder()
    {
        var context = Context(categoryExists: false);

        await context.Service.Create(context.Client, new("valid", "description", "model_a", 999, 31, 9));

        var created = Assert.Single(context.Rooms.Created);
        Assert.Equal(("valid", "description", 36, 10, 0, "model_a"), created);
        Assert.Equal(ServerPacketHeader.FlatCreatedComposer, Assert.Single(context.Sent).Header);
        Assert.Equal(new[] { "reward:1", "messenger:1" }, context.Events);
    }

    [Fact]
    public async Task AvailabilityUsesCanonicalOwnerLoaderAndConfiguredLimit()
    {
        var context = Context(existingRooms: 2);

        await context.Service.SendCreationAvailability(context.Client);

        Assert.Equal(42, context.Loader.LastOwnerId);
        Assert.Equal(ServerPacketHeader.CanCreateRoomComposer, Assert.Single(context.Sent).Header);
    }

    private static TestContext Context(
        int existingRooms = 0,
        string filteredName = "valid",
        bool categoryExists = true,
        bool createSucceeds = true)
    {
        var habbo = new Habbo
        {
            Id = 42,
            Username = "Dennis",
            Messenger = new HabboMessenger([], [], [])
        };
        var (client, sent) = HabbiconTestSupport.Client(habbo);
        habbo.Client = client;
        var events = new List<string>();
        habbo.Messenger.StatusUpdated += (_, _) => events.Add($"messenger:{sent.Count}");
        var model = new RoomModel("model_a", 0, 0, 0, 0, "0", 0, 0, false);
        var roomManagerState = new RoomManagerState();
        var roomManager = Proxy<IRoomManager>((method, args) =>
        {
            if (method == nameof(IRoomManager.TryGetModel))
            {
                args[1] = model;
                return true;
            }
            if (method == nameof(IRoomManager.CreateRoom))
            {
                var request = ((string)args[1], (string)args[2], (int)args[3], (int)args[4], (int)args[5], ((RoomModel)args[6]).Id);
                roomManagerState.Created.Add(request);
                return createSucceeds ? Room(81, model) : null;
            }
            throw new NotSupportedException(method);
        });
        var loader = new RecordingLoader(existingRooms, model);
        var category = new SearchResultList(17, "category", "rooms", "Rooms", false, 0, "", NavigatorViewMode.Regular, "category", "nothing", 0);
        var navigator = Proxy<INavigatorManager>((method, args) =>
        {
            if (method != nameof(INavigatorManager.TryGetSearchResultList))
                throw new NotSupportedException(method);
            args[1] = categoryExists ? category : null;
            return categoryExists;
        });
        var filter = Proxy<IWordFilterManager>((method, args) => method == nameof(IWordFilterManager.CheckMessage)
            ? (string)args[0] == "valid" ? filteredName : args[0]
            : throw new NotSupportedException(method));
        var rewards = Proxy<IRewardTrackManager>((method, _) =>
        {
            if (method != nameof(IRewardTrackManager.Progress))
                throw new NotSupportedException(method);
            events.Add($"reward:{sent.Count}");
            return null;
        });
        var service = new RoomCreationService(loader, roomManager, new Settings(), navigator, filter, rewards, new AccountSessionGate());
        return new(service, client, sent, events, loader, roomManagerState);
    }

    private static RoomData Room(uint id, RoomModel model) => new(
        id, "valid", model.Id, "Dennis", 42, "", 0, "public", "open", 0, 20, 17,
        "description", "", "0.0", "0.0", true, true, false, false, 0, 0, "0.0",
        1, 1, 1, 1, 1, 1, 1, 8, 1, true, true, true, true, true, true, true, 0, 0, true, model);

    private static T Proxy<T>(Func<string, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> InvokeMethod { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!.Name, args!);
    }

    private sealed class RecordingService : IRoomCreationService
    {
        public bool AvailabilityRequested { get; private set; }
        public RoomCreationRequest? Request { get; private set; }
        public Task SendCreationAvailability(GameClient session) { AvailabilityRequested = true; return Task.CompletedTask; }
        public Task Create(GameClient session, RoomCreationRequest request) { Request = request; return Task.CompletedTask; }
    }

    private sealed class RecordingLoader(int count, RoomModel model) : IRoomDataLoader
    {
        public int LastOwnerId { get; private set; }
        public bool TryGetData(uint roomId, out RoomData? data) { data = null; return false; }
        public List<RoomData> GetRoomsDataByOwnerSortByName(int ownerId)
        {
            LastOwnerId = ownerId;
            return Enumerable.Range(1, count).Select(id => Room((uint)id, model)).ToList();
        }
    }

    private sealed class Settings : ISettingsManager
    {
        public string TryGetValue(string value) => "";
        public string? GetOptionalValue(string key) => key.Contains("rooms") ? "2" : "30";
        public Task Reload() => Task.CompletedTask;
    }

    private sealed class RoomManagerState
    {
        public List<(string Name, string Description, int Category, int Visitors, int Trade, string Model)> Created { get; } = [];
    }

    private sealed record TestContext(
        RoomCreationService Service,
        GameClient Client,
        List<(uint Header, byte[] Payload)> Sent,
        List<string> Events,
        RecordingLoader Loader,
        RoomManagerState Rooms);
}
