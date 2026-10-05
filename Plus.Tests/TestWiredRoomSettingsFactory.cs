using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Rooms;
using Plus.Database;
using System.Data;

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
    public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration) { }
}

internal sealed class TestWiredDatabase : IDatabase
{
    public static TestWiredDatabase Instance { get; } = new();
    public bool IsConnected() => throw new NotSupportedException();
    public IDbConnection Connection() => throw new InvalidOperationException("Unused Wired persistence must remain lazy.");
}
