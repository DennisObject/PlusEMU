using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableRuntimeFrameTests
{
    [Fact]
    public void HolderUniverseIgnoresQuantityCapsWhileSourceOperandsStillApplyThem()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var items = Enumerable.Range(1, 3).Select(id => new Item { Id = (uint)id, OwnerId = 5 }).ToList();
        var users = Enumerable.Range(1, 3).Select(id => new RoomUser(100 + id, 0, id, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)).ToList();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Enter), new(() => items, () => users), new UnusedOperations());
        context.Policy.Addons.FurniLimit = 1;
        context.Policy.Addons.UserLimit = 1;

        var frame = WiredVariableRuntimeFrames.Create(context);
        Assert.Same(context, frame.RuntimeContext);
        Assert.Equal(3, frame.Holders.Count(x => x.Target == WiredVariableTarget.Furni));
        Assert.Equal(3, frame.Holders.Count(x => x.Target == WiredVariableTarget.User));
        Assert.Single(frame.ResolveSource!(WiredVariableTarget.Furni, WiredSources.AllRoom, []));
        Assert.Single(frame.ResolveSource!(WiredVariableTarget.User, WiredSources.AllRoom, []));

        // Raw still enforces captured object identities; a replacement cannot inherit the former occupant's membership.
        items[0] = new Item { Id = 1, OwnerId = 5 };
        users[0] = new RoomUser(999, 0, 1, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused);
        var refreshed = WiredVariableRuntimeFrames.Create(context, frame);
        Assert.DoesNotContain(refreshed.Holders, x => x.EntityId == 1);
        Assert.Same(frame.Context, refreshed.Context);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CapturedWallTriggerAndSignalCanHoldVariablesWithoutJoiningAllRoomSelection(bool signal)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        var floor = new Item { Id = 1, OwnerId = 5, Definition = new() { Type = ItemType.Floor } };
        var wall = new Item { Id = 2, OwnerId = 5, Definition = new() { Type = ItemType.Wall } };
        var live = new Dictionary<uint, Item> { [floor.Id] = floor, [wall.Id] = wall };
        var parent = new WiredRuntimeContext(room, new(WiredEventKind.ClickFurni) { EventItem = wall },
            new(() => [floor], () => [], id => live.GetValueOrDefault(id)), new UnusedOperations());
        parent.Triggering.FurniIds.UnionWith([floor.Id, wall.Id]);
        var context = signal ? parent.Fork(new(WiredEventKind.Signal) { EventItem = floor }, 1) : parent;

        if (signal) {
            context.Signal = new(new([wall.Id]), new Dictionary<string, long>());
        }

        context.Policy.Addons.FurniLimit = 1;
        var frame = WiredVariableRuntimeFrames.Create(context);
        var holder = WiredVariableRuntimeFrames.FurniHolder(wall);
        Assert.True(frame.Contains(holder));
        Assert.Equal(floor.Id, (uint)Assert.Single(frame.ResolveSource!(WiredVariableTarget.Furni, WiredSources.AllRoom, [])).EntityId);
        Assert.Equal(2, frame.Holders.Count);
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1)));
        var reference = new WiredVariableReference(WiredVariableTarget.Furni, "custom:10");
        Assert.True(module.Mutate(reference, holder, WiredVariableMutation.Give, 7, frame));
        Assert.Equal(7, module.Read(reference, holder, frame)!.Value);
        live.Remove(wall.Id);
        Assert.Null(module.Read(reference, holder, WiredVariableRuntimeFrames.Create(context, frame)));
        live[wall.Id] = new Item { Id = wall.Id, OwnerId = 5, Definition = wall.Definition };
        Assert.False(WiredVariableRuntimeFrames.Create(context, frame).Contains(holder));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
        public WiredVariableDefinition? Find(uint itemId) => itemId == 10
            ? new(10, 1, 5, "wall", WiredVariableTarget.Furni, WiredVariableAvailability.RoomActive, true) : null;
    }

    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
