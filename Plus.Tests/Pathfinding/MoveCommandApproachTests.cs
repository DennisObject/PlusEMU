using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

public class MoveCommandApproachTests
{
    [Fact]
    public void MoveCommandLeavesAnOptionalImmutableApproachDescriptorForTheNextStackLayer()
    {
        var descriptor = new ApproachDescriptor(42, 7, new(3, 42, SurfaceKind.Top), 1);
        var command = new MoveCommand(9, 3, 0, MoveOrigin.User, Approach: descriptor);
        var slot = new MoveCommandSlot();
        Assert.True(slot.Publish(command));
        Assert.Same(descriptor, slot.Read()!.Approach);
        Assert.Equal(new SurfaceRef(3, 42, SurfaceKind.Top), descriptor.ApproachSurfaceRef);
        Assert.Equal(7, descriptor.ItemRecordVersion);
        Assert.Null(new MoveCommand(10, 1, 1, MoveOrigin.User).Approach);
    }
}
