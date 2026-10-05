using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Data.Moodlight;
using Plus.HabboHotel.Items.Data.Toner;
using Plus.Core.Language;
using Plus.Communication.Packets;
using Plus.HabboHotel.Permissions;
using Plus.HabboHotel.Users.Messenger;

namespace Plus.Tests;

internal static class TestLogging
{
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Configure()
    {
        Dapper.SqlMapper.AddTypeHandler(new Plus.Database.UtcDateTimeOffsetHandler());
        Plus.Core.ExceptionLogger.Configure(Factory);
        Plus.Core.ConsoleCommands.Configure(Factory);
    }

    internal static ILogger Logger => NullLogger.Instance;
    internal static ILogger<GameClient> GameClient => NullLogger<GameClient>.Instance;
    internal static ILogger<RoomNavigation> Navigation => NullLogger<RoomNavigation>.Instance;
    internal static ILogger<WiredRewardService> Rewards => NullLogger<WiredRewardService>.Instance;
    internal static ILoggerFactory Factory => NullLoggerFactory.Instance;
    internal static ILogger<T> For<T>() => NullLogger<T>.Instance;
}

internal sealed class TestRoomFactory : IRoomFactory
{
    public Room Create(RoomData data) => throw new NotSupportedException();
    public void Dispose(uint roomId) { }
}

internal sealed class TestGameClientManager(Func<int, GameClient?> lookup) : IGameClientManager
{
    internal static TestGameClientManager Empty { get; } = new(_ => null);
    public GameClient? GetClientByUserId(int userId) => lookup(userId);
    public int Count => throw new NotSupportedException();
    public ICollection<GameClient> GetClients => throw new NotSupportedException();
    public void OnCycle() => throw new NotSupportedException();
    public GameClient? GetClientByUsername(string username) => throw new NotSupportedException();
    public bool TryGetClient(Guid clientId, out GameClient? client) => throw new NotSupportedException();
    public bool TryChangeClientUsername(GameClient client, string oldUsername, string newUsername, Func<bool> persist) => throw new NotSupportedException();
    public Task<string> GetNameById(int id) => throw new NotSupportedException();
    public IEnumerable<GameClient> GetClientsById(Dictionary<int, MessengerBuddy>.KeyCollection users) => throw new NotSupportedException();
    public void StaffAlert(IServerPacket message, int exclude = 0) => throw new NotSupportedException();
    public void ModAlert(string message) => throw new NotSupportedException();
    public void DoAdvertisingReport(GameClient reporter, GameClient target) => throw new NotSupportedException();
    public void SendPacket(IServerPacket packet, PermissionDefinition? permission = null) => throw new NotSupportedException();
    public void LogClonesOut(int userId) => throw new NotSupportedException();
    public void RegisterClient(GameClient client, int userId, string username) => throw new NotSupportedException();
    public void UnregisterClient(GameClient client, int userId, string username) => throw new NotSupportedException();
    public void CloseAll() => throw new NotSupportedException();
}

internal sealed class TestLanguageManager(IReadOnlyDictionary<string, string> values) : ILanguageManager
{
    internal static TestLanguageManager RoomItems { get; } = new(new Dictionary<string, string>
    {
        ["room.item.already_placed"] = "room.item.already_placed"
    });
    public string TryGetValue(string value) => values.TryGetValue(value, out var translated)
        ? translated
        : throw new KeyNotFoundException(value);
    public Task Reload() => throw new NotSupportedException();
}

internal sealed class TestRoomItemStore : IRoomItemStore
{
    internal static TestRoomItemStore Instance { get; } = new();
    public void AssignOwner(uint itemId, int userId) { }
    public void ClearRoom(uint itemId) { }
    public void SaveWallPosition(uint itemId, string wallPosition) { }
    public void SaveMoved(IReadOnlyList<RoomItemSave> items) { }
    public void PlaceFloor(uint itemId, uint roomId, int x, int y, double z, int rotation) { }
    public void PlaceWall(uint itemId, uint roomId, int x, int y, double z, int rotation, string wallPosition) { }
}

internal sealed class TestRoomItemMetadataStore : IRoomItemMetadataStore
{
    internal static TestRoomItemMetadataStore Instance { get; } = new();
    public void SetMannequinData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
    public void SetToner(uint itemId, uint roomId, int hue, int saturation, int lightness) => throw new NotSupportedException();
    public void SetBrandingData(uint itemId, uint roomId, string data) => throw new NotSupportedException();
    public MoodlightRecord? LoadMoodlight(uint itemId) => throw new NotSupportedException();
    public void SetMoodlightEnabled(uint itemId, uint roomId, bool enabled) => throw new NotSupportedException();
    public void UpdateMoodlightPreset(uint itemId, uint roomId, int preset, string value) => throw new NotSupportedException();
    public TonerRecord? LoadToner(uint itemId) => throw new NotSupportedException();
}

internal sealed class TestRoomUserStore : IRoomUserStore
{
    internal static TestRoomUserStore Instance { get; } = new();
    public void UpdateUserCount(uint roomId, int count) { }
    public void SavePet(RoomPetSave pet) { }
    public void SaveBot(RoomBotSave bot) { }
    public void RecordExit(uint roomId, int userId, DateTimeOffset exitedAt, int usersNow) { }
}

internal sealed class TestRoomDataLoaderFactory : IRoomDataLoaderFactory
{
    public IRoomDataLoader Create(IRoomManager rooms) => new RoomDataLoader(
        EditorTestSupport.UntouchableDatabase(), rooms, null!, null!);
}
