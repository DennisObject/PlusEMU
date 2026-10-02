using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableRuntimeFrameTests
{
    [Fact]
    public void HolderUniverseIgnoresQuantityCapsWhileSourceOperandsStillApplyThem()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        var items = Enumerable.Range(1, 3).Select(id => new Item { Id = (uint)id, OwnerId = 5 }).ToList();
        var users = Enumerable.Range(1, 3).Select(id => new RoomUser(100 + id, 0, id, room)).ToList();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Enter), new(() => items, () => users), new UnusedOperations());
        context.Policy.Addons.FurniLimit = 1; context.Policy.Addons.UserLimit = 1;

        var frame = WiredVariableRuntimeFrames.Create(context);
        Assert.Equal(3, frame.Holders.Count(x => x.Target == WiredVariableTarget.Furni));
        Assert.Equal(3, frame.Holders.Count(x => x.Target == WiredVariableTarget.User));
        Assert.Single(frame.ResolveSource!(WiredVariableTarget.Furni, WiredSources.AllRoom, []));
        Assert.Single(frame.ResolveSource!(WiredVariableTarget.User, WiredSources.AllRoom, []));

        // Raw still enforces captured object identities; a replacement cannot inherit the former occupant's membership.
        items[0] = new Item { Id = 1, OwnerId = 5 };
        users[0] = new RoomUser(999, 0, 1, room);
        var refreshed = WiredVariableRuntimeFrames.Create(context, frame);
        Assert.DoesNotContain(refreshed.Holders, x => x.EntityId == 1);
        Assert.Same(frame.Context, refreshed.Context);
    }

    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
