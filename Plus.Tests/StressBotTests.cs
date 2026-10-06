using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI.Types;
using Plus.HabboHotel.Rooms.Chat.Commands;
using Plus.HabboHotel.Rooms.Chat.Commands.Moderator;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Permissions;
using Xunit;

namespace Plus.Tests;

public class StressBotTests
{
    [Theory]
    [InlineData("bots", "500", true, 500)]
    [InlineData("BOTS", "clear", true, 0)]
    [InlineData("bots", "1000", true, 1000)]
    [InlineData("bots", "1001", false, 1001)]
    [InlineData("bots", "0", false, 0)]
    [InlineData("bots", "-1", false, -1)]
    [InlineData("bots", "2147483648", false, 0)]
    [InlineData("bots", "1.5", false, 0)]
    [InlineData("users", "500", false, 0)]
    public void ValidatesEntityAndCount(string entity, string input, bool valid, int amount)
    {
        Assert.Equal(valid, StressCommand.TryParse(new[] { entity, input }, out var result));
        Assert.Equal(amount, result);
    }

    [Fact]
    public void RejectsMissingAndExtraArguments()
    {
        Assert.False(StressCommand.TryParse(Array.Empty<string>(), out _));
        Assert.False(StressCommand.TryParse(new[] { "bots" }, out _));
        Assert.False(StressCommand.TryParse(new[] { "bots", "500", "extra" }, out _));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RequiresModeratorRightAndEstablishedStaffPermission(bool modTool, bool staffCommand, bool allowed)
    {
        var permissions = UserAccess.Create([], [.. (modTool ? new[] { new UserPermissionOverride(PermissionKeys.ModerationTool, false) } : []),
            .. (staffCommand ? new[] { new UserPermissionOverride(PermissionKeys.CommandStress, false) } : [])]);
        Assert.Equal(allowed, StressCommand.CanExecute(permissions));
    }

    [Fact]
    public void ExistingCommandRegistrationDiscoversStressCommand()
    {
        var services = new ServiceCollection();
        Plus.Program.AddAssignableTo<ICommandBase>(services, typeof(StressCommand).Assembly);
        using var provider = services.BuildServiceProvider();
        Assert.IsType<StressCommand>(provider.GetRequiredService<StressCommand>());
        // Scrutor registers interfaces through factories resolving the concrete singleton.
        Assert.Contains(services, service => service.ServiceType == typeof(ICommandBase) &&
            service.ImplementationFactory?.Invoke(new StressOnlyProvider()) is StressCommand);
    }

    [Fact]
    public void CreatesFiveHundredTemporaryGenericBotsThenCapsAndClearsOnlyThem()
    {
        var (manager, map) = CreateRoom();
        var replies = new List<string>();
        Assert.True(manager.QueueStressBots(500, 7, replies.Add));
        Assert.Empty(manager.GetUserList());
        manager.ProcessStressBots();

        var bots = manager.GetUserList().ToList();
        Assert.Equal(500, bots.Count);
        Assert.Equal(500, bots.Select(bot => bot.VirtualId).Distinct().Count());
        Assert.Equal(500, bots.Select(bot => bot.BotData.Id).Distinct().Count());
        Assert.All(bots, bot =>
        {
            Assert.True(bot.BotData.IsTemporary);
            Assert.True(bot.BotData.Id < 0);
            Assert.Equal(7, bot.BotData.OwnerId);
            Assert.True(bot.AllowOverride);
            Assert.IsType<GenericBot>(bot.BotAi);
            Assert.Equal("freeroam", bot.BotData.WalkingMode);
            Assert.True(bot.BotData.AutomaticChat);
            Assert.NotEmpty(bot.BotData.RandomSpeech);
            Assert.Equal(bot.VirtualId, bot.BotData.VirtualId);
        });
        Assert.Contains("Created 500", replies.Single());

        Assert.True(manager.QueueStressBots(500, 7, replies.Add));
        manager.ProcessStressBots();
        Assert.True(manager.QueueStressBots(1, 7, replies.Add));
        manager.ProcessStressBots();
        Assert.Equal(1000, manager.GetUserList().Count);
        Assert.Contains("limit is 1000", replies.Last());

        // Use an ordinary bot as a control; clearing must preserve it.
        var regularData = bots[0].BotData;
        var speeches = regularData.RandomSpeech;
        var regular = manager.DeployBot(new(42, 0, "generic", "stand", "Regular", "", regularData.Look,
            1, 1, 0, 0, 0, 0, 0, 0, ref speeches, "M", 0, 1, false, 60, false, 0), null!);
        foreach (var bot in manager.GetUserList())
            map.AddUserToMap(bot, bot.Coordinate);
        Assert.True(manager.QueueStressBots(0, 7, replies.Add));
        manager.ProcessStressBots();
        Assert.Same(regular, Assert.Single(manager.GetUserList()));
        Assert.Same(regular, Assert.Single(map.GetRoomUsers(regular.Coordinate)));
        Assert.Contains("Cleared 1000", replies.Last());
    }

    [Fact]
    public void ConcurrentRequestsHaveABoundedBacklog()
    {
        var (manager, _) = CreateRoom();
        var accepted = 0;
        Parallel.For(0, 40, _ =>
        {
            if (manager.QueueStressBots(500, 7, _ => { }))
                Interlocked.Increment(ref accepted);
        });
        Assert.Equal(8, accepted);
        Assert.Empty(manager.GetUserList());
        for (var i = 0; i < accepted; i++)
            manager.ProcessStressBots();
        Assert.Equal(1000, manager.GetUserList().Count);
    }

    [Fact]
    public void TemporaryBotsCanRequestAndFindAPathThroughCrowdedTiles()
    {
        var (manager, map) = CreateRoom();
        manager.QueueStressBots(500, 7, _ => { });
        manager.ProcessStressBots();
        var bots = manager.GetUserList().ToList();
        foreach (var bot in bots.Skip(1))
            map.AddUserToMap(bot, new(2, 2));
        var walking = bots[0];
        walking.MoveTo(2, 2, walking.BotData.IsTemporary);
        Assert.Equal(2, walking.GoalX);
        Assert.Equal(2, walking.GoalY);
        Assert.True(walking.PathRecalcNeeded);
        Assert.NotEmpty(PathFinder.FindPath(walking, true, map, new(walking.X, walking.Y), new(2, 2)));
    }

    [Fact]
    public async Task ConcurrentNormalAndStressDeploymentsKeepVirtualLookupAndClearConsistent()
    {
        var (manager, _) = CreateRoom();
        using var start = new Barrier(2);
        manager.QueueStressBots(500, 7, _ => { });
        var stress = Task.Run(() =>
        {
            start.SignalAndWait();
            manager.ProcessStressBots();
        });
        var normal = Task.Run(() =>
        {
            start.SignalAndWait();
            Parallel.For(1, 501, id =>
            {
                var speeches = new List<Plus.HabboHotel.Rooms.AI.Speech.RandomSpeech>();
                manager.DeployBot(new(id, 0, "generic", "stand", $"Regular {id}", "", "hd-180-1",
                    1, 1, 0, 0, 0, 0, 0, 0, ref speeches, "M", 0, 7, false, 60, false, 0), null!);
            });
        });
        await Task.WhenAll(stress, normal);

        Assert.Equal(1000, manager.GetUserList().Count);
        Assert.All(manager.GetUserList(), user =>
        {
            Assert.Equal(user.VirtualId, user.InternalRoomId);
            Assert.Same(user, manager.GetRoomUserByVirtualId(user.VirtualId));
        });
        manager.QueueStressBots(0, 7, _ => { });
        manager.ProcessStressBots();
        Assert.Equal(500, manager.GetUserList().Count);
        Assert.All(manager.GetUserList(), user => Assert.False(user.BotData.IsTemporary));
    }

    // Bypass Room's DB-loading constructor while retaining its real user manager and map.
    private static (RoomUserManager Manager, Gamemap Map) CreateRoom()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var model = new RoomModel("test", 1, 1, 0, 0, "0000\r0000\r0000\r0000", 0, 0, false);
        var map = new Gamemap(room, model, TestLogging.Navigation, TestRoomSettings.Empty, TestGroupManager.Empty, TestNavigationDatabase.Instance, TestNavigationRewards.Instance);
        var gameMap = new byte[4, 4];
        for (var x = 0; x < 4; x++)
        for (var y = 0; y < 4; y++)
            gameMap[x, y] = 1;
        typeof(Gamemap).GetProperty(nameof(Gamemap.GameMap))!.SetValue(map, gameMap);
        var manager = new RoomUserManager(room, TestRoomUserStore.Instance, TimeProvider.System, new TestRewardProgress(), TestChatEmotions.Unused,
            new TestBotAiFactory((_, virtualId) => new GenericBot(virtualId, new FakeWordFilter())), TestGameClientManager.Empty, TestItemRuntime.Travel);
        typeof(Room).GetField("_gamemap", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, map);
        typeof(Room).GetField("_roomUserManager", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(room, manager);
        TestRoomUserSnapshots.Install(room);
        return (manager, map);
    }

    private sealed class StressOnlyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(StressCommand) ? new StressCommand() : new object();
    }
}
