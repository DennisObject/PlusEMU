using MySqlConnector;
using Plus.Communication.Packets.Incoming.Rooms.FloorPlan;
using Plus.Database.Adapter;
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

    [Fact]
    public void RunQueryRequiredPropagatesExecutionFailures()
    {
        var adapter = new OpenlessQuery();
        adapter.SetQuery("UPDATE `rooms` SET `model_name` = 'model_bc_1' WHERE `id` = 1");

        var swallowed = Record.Exception(() => adapter.RunQuery());
        var required = Record.Exception(() => adapter.RunQueryRequired());

        Assert.Null(swallowed);
        Assert.NotNull(required);

        adapter.DbEnabled = false;
        adapter.SetQuery("UPDATE `room_models` SET `heightmap` = '00' WHERE `id` = 'model_bc_1'");
        Assert.Null(Record.Exception(() => adapter.RunQuery()));
        Assert.Throws<InvalidOperationException>(() => adapter.RunQueryRequired());
    }

    private sealed class OpenlessQuery : QueryAdapter
    {
        public OpenlessQuery() : base(null!)
        {
            Command = new MySqlCommand();
        }
    }
}
