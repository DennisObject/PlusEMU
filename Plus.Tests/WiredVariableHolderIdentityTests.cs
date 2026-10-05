using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableHolderIdentityTests
{
    [Theory]
    [InlineData(2147483648u)]
    [InlineData(uint.MaxValue)]
    public void PermanentHighIdKeepsDurableIdentityAndDetachedCleanupNeverInfersTemporary(uint id)
    {
        var permanent = new Item { Id = id, OwnerId = 5, Definition = new() };
        var temporary = new Item { Id = id, IsTemporary = true, Definition = new() };
        var holder = WiredVariableRuntimeFrames.FurniHolder(permanent);
        var transient = WiredVariableRuntimeFrames.FurniHolder(temporary);
        Assert.Equal(unchecked((int)id), holder.EntityId); Assert.Equal(holder.EntityId, transient.EntityId);
        Assert.Equal((long)id, holder.StableId); Assert.Equal((long)id, holder.StorageId);
        Assert.True(holder.CanPersist); Assert.False(holder.IsTemporaryFurni);
        Assert.Equal(0, transient.StableId); Assert.Equal((long)transient.EntityId, transient.StorageId);
        Assert.False(transient.CanPersist); Assert.True(transient.IsTemporaryFurni);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 1;
        var handling = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards); typeof(Room).GetField("_roomItemHandling", flags)!.SetValue(room, handling);
        var floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", flags)!.GetValue(handling)!;
        floor[id] = permanent;
        var db = DispatchProxy.Create<IDatabase, ModernWiredRuntimeTests.RecordingProxy>();
        ((ModernWiredRuntimeTests.RecordingProxy)(object)db).InvokeMethod = (method, _) => throw new InvalidOperationException("Unexpected SQL: " + method.Name);
        var variables = new WiredRoomVariables(room, db, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)));
        var durable = new MemoryWiredVariableStore(); var module = new WiredVariableModule(1, new Directory(), durable, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)));
        typeof(WiredRoomVariables).GetField("<Module>k__BackingField", flags)!.SetValue(variables, module);
        var frame = new WiredVariableFrame(1, [holder, transient]);
        var active = new WiredVariableReference(WiredVariableTarget.Furni, "custom:10");
        var stored = new WiredVariableReference(WiredVariableTarget.Furni, "custom:11");
        Assert.Equal(unchecked((int)id), new RoomWiredBuiltinVariables(room).Read(new(WiredVariableTarget.Furni, "internal:@id"), holder, frame)!.Value);
        Assert.True(module.Mutate(active, holder, WiredVariableMutation.Give, 10, frame));
        Assert.True(module.Mutate(active, transient, WiredVariableMutation.Give, 20, frame));
        Assert.True(module.Mutate(stored, holder, WiredVariableMutation.Give, 30, frame));
        Assert.False(module.Mutate(stored, transient, WiredVariableMutation.Give, 40, frame));
        Assert.Equal((long)id, Assert.Single(durable.GetHolders(11)).Key.HolderId);
        variables.ItemDetached(permanent);
        Assert.Null(module.Read(active, holder, frame)); Assert.Equal(20, module.Read(active, transient, frame)!.Value);
        Assert.Equal(30, module.Read(stored, holder, frame)!.Value);
        Assert.True(module.Mutate(active, holder, WiredVariableMutation.Give, 50, frame));
        floor.TryRemove(id, out _); variables.ItemDetached(id);
        Assert.Null(module.Read(active, holder, frame)); Assert.Equal(20, module.Read(active, transient, frame)!.Value);
        variables.ItemDetached(temporary); Assert.Null(module.Read(active, transient, frame));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
        public WiredVariableDefinition? Find(uint itemId) => itemId is 10 or 11
            ? new(itemId, 1, 5, "value", WiredVariableTarget.Furni,
                itemId == 11 ? WiredVariableAvailability.Persistent : WiredVariableAvailability.RoomActive, true) : null;
    }
}
