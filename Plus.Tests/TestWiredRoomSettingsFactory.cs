using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using System.Data;
using Plus.HabboHotel.Rooms.AI;

namespace Plus.Tests;

internal sealed class TestWiredRoomSettingsFactory : IWiredRoomSettingsFactory
{
    public static TestWiredRoomSettingsFactory Instance { get; } = new();
    public WiredRoomSettings Create(Room room) => new(room, Store.Instance);

    private sealed class Store : IWiredRoomSettingsStore
    {
        public static Store Instance { get; } = new();
        public WiredRoomSettingsSnapshot? Load(uint roomId) => null;
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings) { }
    }
}

internal sealed class TestWiredConfigurationStore : IWiredConfigurationStore
{
    public static TestWiredConfigurationStore Instance { get; } = new();
    public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => null;
    public void Reset(IReadOnlyCollection<uint> itemIds) { }
    public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) { }
}

internal sealed class TestWiredRewardService : IWiredRewardService
{
    public static TestWiredRewardService Instance { get; } = new();
    public bool Execute(Item box, WiredRuntimeContext context, WiredConfiguration config) => throw new InvalidOperationException("Unused Wired reward service must remain lazy.");
}

internal sealed class TestBotManagementStore : IBotManagementStore
{
    public static TestBotManagementStore Instance { get; } = new();
    public BotPlacementData Place(int botId, int ownerId, uint roomId, int x, int y) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
    public void PickUp(int botId, uint roomId) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
    public void SaveAppearance(int botId, uint roomId, string look, string gender) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
    public IReadOnlyList<string> SaveSpeech(int botId, uint roomId, IReadOnlyList<string> speech,
        bool automatic, int interval, bool mix) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
    public void SaveWalkingMode(int botId, uint roomId, string mode) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
    public void SaveName(int botId, uint roomId, string name) =>
        throw new InvalidOperationException("Unused Wired bot persistence must remain lazy.");
}

internal sealed class TestWiredDatabase : IDatabase
{
    public static TestWiredDatabase Instance { get; } = new();
    public bool IsConnected() => throw new NotSupportedException();
    public IDbConnection Connection() => throw new InvalidOperationException("Unused Wired persistence must remain lazy.");
}
