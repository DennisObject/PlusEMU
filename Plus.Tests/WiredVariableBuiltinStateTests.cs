using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableBuiltinStateTests
{
    [Fact]
    public void ChangedBuiltinStateNotifiesOnceAfterNativeUpdateAndKeepsFiringFrame()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 1;
        var handling = new RoomItemHandling(room, TestRoomItemStore.Instance); typeof(Room).GetField("_roomItemHandling", flags)!.SetValue(room, handling);
        typeof(Room).GetField("_roomUserManager", flags)!.SetValue(room, new RoomUserManager(room));
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", flags)!.GetValue(handling)!;
        var item = new Item { Id = 20, OwnerId = 5, ExtraData = new LegacyDataFormat { Data = "0" }, Definition = new() { Modes = 3, Type = ItemType.Floor } };
        typeof(Item).GetField("_room", flags)!.SetValue(item, room); floor[item.Id] = item;
        var holder = WiredVariableRuntimeFrames.FurniHolder(item); var frame = new WiredVariableFrame(1, [holder]) { Depth = 3 };
        var notices = 0;
        var builtins = new RoomWiredBuiltinVariables(room, stateChanged: (changed, firing) =>
        {
            Assert.Same(item, changed); Assert.Same(frame, firing); Assert.Equal("1", changed.LegacyDataString);
            var moved = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_movedItems", flags)!.GetValue(handling)!;
            Assert.Same(item, moved[item.Id]); notices++;
        });
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), () => 1, builtins);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "internal:@state");
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 1, frame));
        Assert.Equal(1, notices);
        var change = Assert.Single(module.DrainChanges()); Assert.Equal(0, change.Before!.Value); Assert.Equal(1, change.After!.Value);
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 1, frame));
        Assert.False(builtins.Write(reference, holder, 1, frame));
        Assert.False(module.Mutate(reference, holder, WiredVariableMutation.Set, 3, frame));
        Assert.False(builtins.Write(reference, holder, 2, new(1, [])));
        Assert.Equal(1, notices); Assert.Empty(module.DrainChanges()); Assert.Equal("1", item.LegacyDataString);
    }
    [Fact]
    public async Task CreatorStateNotificationDoesNotHoldModuleLockWhileEnteringEngine()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 1;
        var handling = new RoomItemHandling(room, TestRoomItemStore.Instance); typeof(Room).GetField("_roomItemHandling", flags)!.SetValue(room, handling);
        typeof(Room).GetField("_roomUserManager", flags)!.SetValue(room, new RoomUserManager(room));
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", flags)!.GetValue(handling)!;
        var item = new Item { Id = 20, OwnerId = 5, ExtraData = new LegacyDataFormat { Data = "0" }, Definition = new() { Modes = 3, Type = ItemType.Floor } };
        typeof(Item).GetField("_room", flags)!.SetValue(item, room); floor[item.Id] = item;
        var holder = WiredVariableRuntimeFrames.FurniHolder(item); var frame = new WiredVariableFrame(1, [holder]);
        Assert.Null(frame.RuntimeContext); // Creator/menu writes do not already own engine execution.
        var engineGate = new object(); using var engineEntered = new ManualResetEventSlim(); using var callbackEntered = new ManualResetEventSlim();
        var notified = false;
        var builtins = new RoomWiredBuiltinVariables(room, stateChanged: (_, _) =>
        {
            callbackEntered.Set();
            if (!Monitor.TryEnter(engineGate, TimeSpan.FromSeconds(2))) throw new TimeoutException("State callback retained module lock while entering engine");
            try { notified = true; } finally { Monitor.Exit(engineGate); }
        });
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), () => 1, builtins);
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "internal:@state");
        var engine = Task.Run(() =>
        {
            lock (engineGate)
            {
                engineEntered.Set(); Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(3)));
                // Mirrors engine-owned FX reading values while a creator state callback enters Dispatch.
                Assert.Equal(1, module.Read(reference, holder, frame)!.Value);
            }
        });
        Assert.True(engineEntered.Wait(TimeSpan.FromSeconds(3)));
        var creator = Task.Run(() => Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Set, 1, frame, origin: 2)));
        await Task.WhenAll(engine, creator).WaitAsync(TimeSpan.FromSeconds(6));
        Assert.True(notified); Assert.Equal(2, Assert.Single(module.DrainChanges()).Origin);
    }
    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
        public WiredVariableDefinition? Find(uint itemId) => null;
    }
}
