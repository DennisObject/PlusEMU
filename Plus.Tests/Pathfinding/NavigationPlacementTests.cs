using Plus.HabboHotel.Items;
using Plus.Tests.Pathfinding;
using Xunit;

namespace Plus.Tests;

public partial class PlacedFurniRoomTests
{
    [Fact]
    public void NewPlacementPublishesOnlyFinalGeometryWhileKeepingThePlacementTransaction()
    {
        var handler = _room.GetRoomItemHandler();
        var navigation = NavTest.Enable(_room.GetGameMap());
        var item = Furni(10, InteractionType.None, Plus.HabboHotel.Items.Wired.WiredBoxType.None);
        item.Definition.Width = 2;
        item.Definition.Height = 0;
        item.Definition.Walkable = true;
        Exception? error = null;
        bool result = false;
        var placement = new Thread(() =>
        {
            try
            {
                result = handler.SetFloorItem(null!, item, 2, 2, 0, true, false, false, height: 1.25);
            }
            catch (Exception e) { error = e; }
        });

        lock (item.NavSync)
        {
            placement.Start();
            Assert.True(SpinWait.SpinUntil(() => (placement.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000));
            // Placement holds PlacementSync and waits to write geometry. Neither room
            // membership nor a record at the inventory item's old coordinates is visible.
            Assert.Null(handler.GetItem(item.Id));
            Assert.Null(navigation.Inputs.Read(item.Id));
            navigation.Compiler.ApplyNow();
            Assert.DoesNotContain(item.Id, navigation.Grid.SupportItem);
        }

        Assert.True(placement.Join(5000));
        Assert.Null(error);
        Assert.True(result);
        navigation.Compiler.ApplyNow();
        Assert.Same(item, handler.GetItem(item.Id));
        Assert.Equal(new[] { 10, 11 }, navigation.Inputs.AppliedRecords[item.Id].Footprint);
        Assert.Equal(1.25, navigation.Grid.WalkZ[10]);
        Assert.Equal(1.25, navigation.Grid.WalkZ[11]);
        Assert.Contains(item, _room.GetGameMap().GetCoordinatedItems(new(2, 2)));
        Assert.Contains(item, _room.GetGameMap().GetCoordinatedItems(new(3, 2)));
    }
}
