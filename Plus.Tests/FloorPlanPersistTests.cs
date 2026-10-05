using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Xunit;

namespace Plus.Tests;

public class FloorPlanPersistTests
{
    [Fact]
    public void FailedModelOrRoomWriteDoesNotCountAsPersisted()
    {
        var steps = new List<string>();

        Assert.False(UpdateFloorPropertiesEvent.TryPersist(
            () =>
            {
                steps.Add("model");
                throw new InvalidOperationException("model");
            },
            () => throw new InvalidOperationException("visible"),
            () => throw new InvalidOperationException("room")));
        Assert.Equal(new[] { "model" }, steps);

        steps.Clear();
        Assert.False(UpdateFloorPropertiesEvent.TryPersist(
            () =>
            {
                steps.Add("model");
                return 0;
            },
            () => throw new InvalidOperationException("visible"),
            () => throw new InvalidOperationException("room")));
        Assert.Equal(new[] { "model" }, steps);

        steps.Clear();
        Assert.False(UpdateFloorPropertiesEvent.TryPersist(
            () =>
            {
                steps.Add("model");
                return 1;
            },
            () =>
            {
                steps.Add("visible");
                return false;
            },
            () => throw new InvalidOperationException("room")));
        Assert.Equal(new[] { "model", "visible" }, steps);

        steps.Clear();
        Assert.False(UpdateFloorPropertiesEvent.TryPersist(
            () => 1,
            () => true,
            () =>
            {
                steps.Add("room");
                throw new InvalidOperationException("rooms");
            }));
        Assert.Equal(new[] { "room" }, steps);

        Assert.False(UpdateFloorPropertiesEvent.TryPersist(() => 1, () => true, () => 0));
        Assert.True(UpdateFloorPropertiesEvent.TryPersist(() => 1, () => true, () => 1));
    }

}
