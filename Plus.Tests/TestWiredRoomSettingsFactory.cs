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
using Plus.Core.Settings;
using Plus.HabboHotel.Groups;
using Plus.HabboHotel.Users;
using System.Diagnostics.CodeAnalysis;

namespace Plus.Tests;

internal sealed class TestRoomSettings(Dictionary<string, string>? values = null) : ISettingsManager
{
    public static TestRoomSettings Empty { get; } = new();
    public Dictionary<string, string> Values { get; } = values ?? [];
    public string TryGetValue(string value) => Values.TryGetValue(value, out var setting) ? setting : "0";
    public string TryGetValue(string value, string defaultValue) => Values.TryGetValue(value, out var setting) ? setting : defaultValue;
    public string? GetOptionalValue(string key) => Values.GetValueOrDefault(key);
    public Task Reload() => throw new InvalidOperationException("Room fixture settings cannot reload.");
}

internal sealed class TestGroupManager(Func<int, Group?>? lookup = null) : IGroupManager
{
    public static TestGroupManager Empty { get; } = new();
    public ICollection<GroupBadgeParts> BadgeBases => throw Unused();
    public ICollection<GroupBadgeParts> BadgeSymbols => throw Unused();
    public ICollection<GroupColours> BadgeBaseColours => throw Unused();
    public ICollection<GroupColours> BadgeSymbolColours => throw Unused();
    public ICollection<GroupColours> BadgeBackColours => throw Unused();
    public void Init() => throw Unused();
    public bool TryGetGroup(int id, [NotNullWhen(true)] out Group? group)
    {
        group = lookup?.Invoke(id);

        return group != null;
    }
    public bool TryCreateGroup(Habbo player, string name, string description, uint roomId, string badge, int colour1,
        int colour2, [NotNullWhen(true)] out Group? group)
    {
        group = null;
        throw Unused();
    }
    public string GetColourCode(int id, bool colourOne) => throw Unused();
    public void DeleteGroup(int id) => throw Unused();
    public List<Group> GetGroupsForUser(int userId) => throw Unused();
    public Dictionary<int, string> GetAllBadgesInRoom(Room room) => throw Unused();
    private static InvalidOperationException Unused() => new("Unused room fixture group operation must remain lazy.");
}

internal sealed class TestWiredRoomSettingsFactory : IWiredRoomSettingsFactory
{
    public static TestWiredRoomSettingsFactory Instance { get; } = new();
    public WiredRoomSettings Create(Room room) => new(room, Store.Instance);

    private sealed class Store : IWiredRoomSettingsStore
    {
        public static Store Instance { get; } = new();
        public WiredRoomSettingsSnapshot? Load(uint roomId) => null;
        public void Save(uint roomId, int actorId, bool staff, WiredRoomSettingsSnapshot? expected, WiredRoomSettingsSnapshot settings)
        {
        }
    }
}

internal sealed class TestWiredConfigurationStore : IWiredConfigurationStore
{
    public static TestWiredConfigurationStore Instance { get; } = new();
    public WiredConfiguration? Load(uint itemId, WiredBoxDescriptor descriptor) => null;
    public void Reset(IReadOnlyCollection<uint> itemIds)
    {
    }
    public void Save(uint itemId, WiredBoxDescriptor descriptor, WiredConfiguration configuration)
    {
    }
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
