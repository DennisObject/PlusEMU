using Plus.HabboHotel.Items.Wired.Settings;
using Plus.HabboHotel.Rooms;

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
