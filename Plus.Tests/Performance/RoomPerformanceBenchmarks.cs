using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Communication.Flash;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Packets.Outgoing.Rooms.Engine;
using Plus.Communication.Packets.Outgoing.Rooms.Chat;
using Plus.Communication.Revisions;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.AI;
using Plus.HabboHotel.Rooms.PathFinding;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests.Performance;

public class RoomPerformanceBenchmarks
{
    [Fact]
    public void MeasureFiveHundredEntitiesInMemory()
    {
        var output = Environment.GetEnvironmentVariable("PLUSEMU_ROOM_BENCHMARK");
        if (string.IsNullOrEmpty(output)) return;
        var bots = RoomPerformanceFixture.Create(500, 1);
        var users = RoomPerformanceFixture.Create(0, 500);
        var noRecipients = RoomPerformanceFixture.Create(500, 0);
        var committedWalking = RoomPerformanceFixture.Create(500, 0);
        var results = new List<string> { $"Runtime: {Environment.Version}; entities: 500; sockets/database/hotel: none" };
        Measure("GetUserList (500 bots + observer)", 10000, () => GC.KeepAlive(bots.Manager.GetUserList()));
        Measure("GetRoomUsers (500 bots + observer)", 10000, () => GC.KeepAlive(bots.Manager.GetRoomUsers()));
        Measure("500 square lookups (empty)", 1000, () => { for (var i = 0; i < 500; i++) GC.KeepAlive(bots.Manager.GetUserForSquare(3, 3)); });
        Measure("500 square lookups (occupied)", 1000, () => { for (var i = 0; i < 500; i++) GC.KeepAlive(bots.Manager.GetUserForSquare(1, 1)); });
        Measure("CycleUsers (500 idle bots)", 1000, noRecipients.Manager.OnCycle);
        Measure("CycleUsers (500 walking bots, precomputed paths)", 1000, () => { noRecipients.PrepareWalking(); noRecipients.Manager.OnCycle(); });
        Measure("Two cycles (500 walking bots, committed steps)", 1000, () =>
        {
            var beforeX = committedWalking.Bots[0].X;
            committedWalking.PrepareNextWalkingStep();
            committedWalking.Manager.OnCycle();
            committedWalking.Manager.OnCycle();
            if (committedWalking.Bots.Any(bot => bot.X == beforeX))
                throw new InvalidOperationException("A benchmark bot did not commit its walking step");
        });
        Measure("500 bot chat messages to observer", 100, () => { foreach (var bot in bots.Bots) bot.Chat("Stress bot checking room traffic."); });
        Measure("500 avatar status updates to observer", 1000, () => { foreach (var bot in bots.Bots) bot.UpdateNeeded = true; bots.Manager.SerializeStatusUpdates(); });
        Measure("500 changed-avatar collection, no recipients", 1000, () => { foreach (var bot in noRecipients.Bots) bot.UpdateNeeded = true; noRecipients.Manager.SerializeStatusUpdates(); });
        Measure("500-avatar single GameClient.Send", 1000, () => users.Clients[0].Send(new UserUpdateComposer(users.Users)));
        Measure("500 small GameClient.Send calls", 1000, () => { for (var i = 0; i < 500; i++) users.Clients[0].Send(new ChatComposer(i, "test", 0, 2)); });
        Measure("500 avatar packet to 500 recipients", 30, () => users.Room.SendPacket(new UserUpdateComposer(users.Users)));
        Measure("500 avatar status updates to 500 recipients", 30, () => { foreach (var user in users.Users) user.UpdateNeeded = true; users.Manager.SerializeStatusUpdates(); });
        File.WriteAllLines(output, results);

        void Measure(string name, int iterations, Action action)
        {
            for (var i = 0; i < 20; i++) action();
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < iterations; i++) action();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds / iterations;
            allocated = (GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations;
            results.Add($"{name}: {elapsed:F4} ms/op; {allocated:N0} B/op; iterations={iterations}");
        }
    }
}

internal sealed class RoomPerformanceFixture
{
    public required Room Room { get; init; }
    public required RoomUserManager Manager { get; init; }
    public required Gamemap Map { get; init; }
    public List<RoomUser> Bots { get; } = new();
    public List<RoomUser> Users { get; } = new();
    public List<FlashGameClient> Clients { get; } = new();

    public static RoomPerformanceFixture Create(int botCount, int userCount, int mapSize = 4)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var map = new Gamemap(room, new RoomModel("benchmark", 0, 0, 0, 0, string.Join('\r', Enumerable.Repeat(new string('0', mapSize), mapSize)), 0, 0, false));
        var grid = new byte[mapSize, mapSize];
        for (var x = 0; x < mapSize; x++) for (var y = 0; y < mapSize; y++) grid[x, y] = 1;
        typeof(Gamemap).GetProperty(nameof(Gamemap.GameMap))!.SetValue(map, grid);
        var manager = new RoomUserManager(room);
        SetField(room, "_gamemap", map);
        SetField(room, "_roomUserManager", manager);
        var fixture = new RoomPerformanceFixture { Room = room, Manager = manager, Map = map };
        var dictionary = (ConcurrentDictionary<int, RoomUser>)typeof(RoomUserManager).GetField("_users", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
        var revision = new Revision { InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>
        {
            [ServerPacketHeader.UserUpdateComposer] = 111,
            [ServerPacketHeader.ChatComposer] = 222,
            [ServerPacketHeader.UserRemoveComposer] = 333
        } };
        var factory = new FlashPacketFactory();
        for (var i = 0; i < botCount + userCount; i++)
        {
            var user = new RoomUser(i + 1, 0, i, room) { X = 1, Y = 1, InternalRoomId = i, AllowOverride = true };
            user.Statusses.Add("mv", "2,1,0");
            if (i < botCount)
            {
                user.BotData = (RoomBot)RuntimeHelpers.GetUninitializedObject(typeof(RoomBot));
                typeof(RoomBot).GetProperty(nameof(RoomBot.IsTemporary))!.SetValue(user.BotData, true);
                user.Path.AddRange(new[] { new Vector2D(3, 1), new Vector2D(2, 1), new Vector2D(1, 1) });
                fixture.Bots.Add(user);
            }
            else
            {
                var client = new FlashGameClient(null!, factory) { Revision = revision, SendCallback = _ => false };
                var habbo = (Habbo)RuntimeHelpers.GetUninitializedObject(typeof(Habbo));
                habbo.CurrentRoom = room;
                client.SetHabbo(habbo);
                SetField(user, "_mClient", client);
                fixture.Clients.Add(client);
                fixture.Users.Add(user);
            }
            dictionary.TryAdd(i, user);
            map.AddUserToMap(user, user.Coordinate);
        }
        // Null AI deliberately avoids bot timer/pathfinding work. Cycle counts those entries.
        manager.UserCount = botCount + userCount;
        return fixture;
    }

    public void PrepareWalking()
    {
        foreach (var bot in Bots)
        {
            bot.X = 1; bot.Y = 1; bot.GoalX = 3; bot.GoalY = 1;
            bot.IsWalking = true; bot.PathStep = 1; bot.SetStep = false;
        }
    }

    public void PrepareNextWalkingStep()
    {
        foreach (var bot in Bots)
        {
            var forward = bot.X == 1;
            bot.GoalX = forward ? 3 : 0;
            bot.GoalY = 1;
            bot.Path[0].X = bot.GoalX;
            bot.Path[1].X = forward ? 2 : 1;
            bot.Path[2].X = bot.X;
            bot.IsWalking = true;
            bot.PathStep = 1;
            bot.SetStep = false;
        }
    }

    public static void SetField(object instance, string field, object value) =>
        instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
}
