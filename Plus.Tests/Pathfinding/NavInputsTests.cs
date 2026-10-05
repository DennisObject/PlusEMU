using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests.Pathfinding;

[Collection("Pathfinding room adapter")]
public class NavInputsTests
{
    [Fact]
    public async Task StalePublisherCannotOverwriteANewerVersion()
    {
        var inputs = new NavInputs(4, 1);
        using var captured = new ManualResetEventSlim(); using var newer = new ManualResetEventSlim();
        var old = Task.Run(() => { var record = NavTest.Record(1, 1, [0]); captured.Set(); Assert.True(newer.Wait(5000)); return inputs.Publish(record); });
        Assert.True(captured.Wait(5000)); Assert.True(inputs.Publish(NavTest.Record(1, 2, [3]))); newer.Set();
        Assert.False(await old); Assert.Equal(2, inputs.Read(1)!.Version); Assert.Equal(new[] { 3 }, inputs.Read(1)!.Footprint);
    }

    [Fact]
    public async Task DirtySetDuringDrainIsNotLost()
    {
        var inputs = new NavInputs(64, 2); inputs.MarkDirty(1);
        using var drained = new ManualResetEventSlim(); using var published = new ManualResetEventSlim();
        var writer = Task.Run(() => { Assert.True(drained.Wait(5000)); inputs.MarkDirty(2); published.Set(); });
        var first = inputs.Drain(word => { if (word == 0) { drained.Set(); Assert.True(published.Wait(5000)); } });
        await writer; Assert.Contains(1, first); Assert.Contains(2, inputs.Drain());
    }

    [Fact]
    public async Task MoveBetweenDrainAndReadRebuildsOldAndNewFootprintsTogether()
    {
        var (grid, inputs, compiler) = NavTest.Create(6, 1);
        inputs.Publish(NavTest.Record(1, 1, [0, 1], h: 1)); compiler.ApplyNow();
        inputs.MarkDirty(0);
        using var drained = new ManualResetEventSlim(); using var moved = new ManualResetEventSlim();
        var writer = Task.Run(() => { Assert.True(drained.Wait(5000)); inputs.Publish(NavTest.Record(1, 2, [4, 5], h: 2)); moved.Set(); });
        compiler.Apply(afterDrain: () => { drained.Set(); Assert.True(moved.Wait(5000)); });
        await writer;
        Assert.Equal(new double[] { 0, 0, 0, 0, 2, 2 }, grid.WalkZ);
        Assert.Equal(2, inputs.AppliedRecords[1].Version);
    }

    [Fact]
    public async Task WriterAfterSelectionIsAppliedOnTheNextBoundary()
    {
        var (grid, inputs, compiler) = NavTest.Create(6, 1);
        inputs.Publish(NavTest.Record(1, 1, [0, 1], h: 1));
        inputs.Publish(NavTest.Record(2, 2, [1, 2], h: 0));
        using var selected = new ManualResetEventSlim(); using var moved = new ManualResetEventSlim();
        var writer = Task.Run(() => { Assert.True(selected.Wait(5000)); inputs.Publish(NavTest.Record(1, 3, [4, 5], h: 2)); moved.Set(); });
        compiler.Apply(beforeRead: id => { if (id == 2) { selected.Set(); Assert.True(moved.Wait(5000)); } });
        await writer;
        // Enumeration order need not be fixed; each footprint must still be coherent.
        Assert.Equal(grid.WalkZ[0], grid.WalkZ[1]); Assert.Equal(grid.WalkZ[4], grid.WalkZ[5]);
        compiler.ApplyNow(); Assert.Equal(new double[] { 0, 0, 0, 0, 2, 2 }, grid.WalkZ);
    }

    [Fact]
    public async Task MutationLockPreventsTornGeometryAndTombstonePreventsResurrection()
    {
        var (grid, inputs, compiler) = NavTest.Create(8, 2);
        var item = NavTest.Item(width: 2); inputs.Attach(item);
        using var halfWritten = new ManualResetEventSlim(); using var finish = new ManualResetEventSlim();
        var writer = Task.Run(() => inputs.Mutate(item, () =>
        {
            item.GetX = 4; halfWritten.Set(); Assert.True(finish.Wait(5000)); item.GetY = 1; item.GetZ = 2; return true;
        }));
        Assert.True(halfWritten.Wait(5000)); compiler.ApplyNow();
        Assert.Equal(new[] { 0, 1 }, inputs.AppliedRecords[item.Id].Footprint);
        finish.Set(); await writer; compiler.ApplyNow();
        Assert.Equal(new[] { 12, 13 }, inputs.AppliedRecords[item.Id].Footprint);
        Assert.Equal(2, grid.WalkZ[12]); Assert.Equal(2, grid.WalkZ[13]);
        inputs.Remove(item); item.GetZ = 8; compiler.ApplyNow();
        Assert.True(inputs.Read(item.Id)!.Removed); Assert.Equal(0, grid.WalkZ[12]);
    }

    [Fact]
    public async Task ConcurrentMutationStressMatchesFinalFullRebuild()
    {
        var (grid, inputs, compiler) = NavTest.Create(16, 16);
        var items = Enumerable.Range(1, 8).Select(i => NavTest.Item((uint)i, width: 2)).ToArray();
        foreach (var item in items) inputs.Attach(item);
        var workers = items.Select(item => Task.Run(() =>
        {
            for (var i = 0; i < 1000; i++) inputs.Mutate(item, () => { item.GetX = i % 14; item.GetY = (i + (int)item.Id) % 16; item.GetZ = item.Id; return true; });
        })).ToArray();
        while (workers.Any(w => !w.IsCompleted)) { compiler.ApplyNow(); await Task.Yield(); }
        await Task.WhenAll(workers); compiler.ApplyNow();
        var z = grid.WalkZ.ToArray(); var flags = grid.Flags.ToArray(); compiler.RebuildAll();
        Assert.Equal(z, grid.WalkZ); Assert.Equal(flags, grid.Flags);
    }
    [Fact]
    public void ReplacementRecordDoesNotAcceptWritesFromDetachedOldInstance()
    {
        var (grid, inputs, compiler) = NavTest.Create(3, 1);
        var old = NavTest.Item(); old.GetZ = 1; inputs.Attach(old); compiler.ApplyNow();
        inputs.Remove(old);
        var replacement = NavTest.Item(); replacement.GetX = 2; replacement.GetZ = 2; inputs.Attach(replacement);
        old.GetX = 1; old.GetZ = 99; compiler.ApplyNow();
        Assert.Equal(new double[] { 0, 0, 2 }, grid.WalkZ);
        Assert.False(inputs.Read(old.Id)!.Removed);
        Assert.Equal(new[] { 2 }, inputs.Read(old.Id)!.Footprint);
    }

    [Fact]
    public void CopiedWorkspaceLeaseCannotReleaseANewLease()
    {
        var first = PathWorkspacePool.Rent(19, 19); var copy = first;
        var workspace = first.Workspace; first.Dispose();
        using var second = PathWorkspacePool.Rent(19, 19);
        Assert.Same(workspace, second.Workspace); copy.Dispose();
        using var third = PathWorkspacePool.Rent(19, 19);
        Assert.NotSame(second.Workspace, third.Workspace);
    }
    [Fact]
    public void RealBulkPickupPublishesTombstonesAndDetachesOldInstances()
    {
        var fixture = Plus.Tests.Performance.RoomPerformanceFixture.Create(0, 1);
        var client = fixture.Clients[0];
        client.Revision.InternalIdToOutgoingIdMapping = new Dictionary<uint, uint>(client.Revision.InternalIdToOutgoingIdMapping)
        {
            [Plus.Communication.Packets.Outgoing.ServerPacketHeader.FurniListAddComposer] = 2020
        };
        var habbo = client.GetHabbo(); habbo.Id = 7;
        habbo.Inventory = new Plus.HabboHotel.Users.Inventory.InventoryComponent
        {
            Furniture = new Plus.HabboHotel.Users.Inventory.Furniture.FurnitureInventoryComponent([], [])
        };
        var floor = (System.Collections.Concurrent.ConcurrentDictionary<uint, Plus.HabboHotel.Items.Item>)
            typeof(Plus.HabboHotel.Rooms.RoomItemHandling).GetField("_floorItems", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(fixture.Room.GetRoomItemHandler())!;
        var item = NavTest.Item(); item.UserId = 7; item.GetX = item.GetY = 2; item.GetZ = 1;
        floor[item.Id] = item;
        var navigation = NavTest.Enable(fixture.Map); navigation.Inputs.Attach(item); navigation.Compiler.RebuildAll();
        Assert.Equal(1, navigation.Grid.WalkZ[10]);
        fixture.Room.GetRoomItemHandler().RemoveItems(client); item.GetZ = 99; navigation.Compiler.ApplyNow();
        Assert.True(navigation.Inputs.Read(item.Id)!.Removed); Assert.Equal(0, navigation.Grid.WalkZ[10]);
        Assert.NotNull(habbo.Inventory.Furniture.GetItem(item.Id));
    }
    [Fact]
    public void DestroyDetachesBeforeClearingFurnitureDefinition()
    {
        var (grid, inputs, compiler) = NavTest.Create(2, 1);
        var item = NavTest.Item(); item.GetZ = 1; inputs.Attach(item); compiler.ApplyNow();
        item.Destroy(); item.GetZ = 99; compiler.ApplyNow();
        Assert.True(inputs.Read(item.Id)!.Removed); Assert.Equal(0, grid.WalkZ[0]);
    }
    [Fact]
    public async Task PickupDoesNotHoldNavigationLockWhileWaitingForWiredFlash()
    {
        var fixture = Plus.Tests.Performance.RoomPerformanceFixture.Create(0, 0);
        var handler = fixture.Room.GetRoomItemHandler();
        var wired = new Plus.HabboHotel.Rooms.Instance.WiredComponent(fixture.Room, TestLogging.Logger, TimeProvider.System,
            TestRoomSettings.Empty, TestWiredRoomSettingsFactory.Instance, TestWiredConfigurationStore.Instance,
            TestWiredDatabase.Instance, TestWiredRewardService.Instance, TestBotManagementStore.Instance, TestWiredClients.Empty, TestGroupManager.Empty);
        Set(fixture.Room, "_wiredComponent", wired);
        var item = NavTest.Item(); item.GetX = item.GetY = 2;
        item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "0" };
        Set(item, "_room", fixture.Room);
        var floor = (System.Collections.Concurrent.ConcurrentDictionary<uint, Plus.HabboHotel.Items.Item>)Get(handler, "_floorItems");
        floor[item.Id] = item; fixture.Map.GenerateMaps();
        var navigation = NavTest.Enable(fixture.Map);
        var engine = Get(wired, "_engine"); var wiredLock = Get(engine, "_sync");
        var remove = handler.GetType().GetMethod("RemoveRoomItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Task pickup;
        bool lockAvailable;
        // Force Wired -> NavSync to overlap pickup -> Wired's real DetachRoomItem.
        lock (wiredLock)
        {
            pickup = Task.Run(() => remove.Invoke(handler, [item]));
            Assert.True(SpinWait.SpinUntil(() => navigation.Inputs.Read(item.Id)?.Removed == true, 5000));
            lockAvailable = Monitor.TryEnter(item.NavSync, 1000);
            if (lockAvailable) Monitor.Exit(item.NavSync);
            if (lockAvailable) wired.OnEvent(item);
        }
        await pickup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(lockAvailable, "Pickup held NavSync while blocked on Wired._sync.");
        Assert.Null(item.NavigationInputs);

        static object Get(object target, string field) => target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(target)!;
        static void Set(object target, string field, object value) => target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);
    }

    [Fact]
    public void DisabledRoomsHaveNoAdapterLockOrSetterAllocations()
    {
        var fixture = Plus.Tests.Performance.RoomPerformanceFixture.Create(0, 0);
        Assert.Null(fixture.Map.Navigation);
        var item = NavTest.Item(); item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat();
        for (var i = 0; i < 100; i++) Change(i);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++) Change(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.False(item.HasNavigationLock); Assert.Null(item.NavigationInputs);
        void Change(int i) { item.GetX = i; item.GetY = i; item.GetZ = i; item.Rotation = 2; item.GroupId = i; item.LegacyDataString = "1"; }
    }

    [Fact]
    public void EnabledUpdatesReuseFootprintsAndSkipCosmeticAndDuplicateRecords()
    {
        var inputs = new NavInputs(4, 4); var item = NavTest.Item();
        item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "0" };
        inputs.Attach(item); var first = inputs.Read(item.Id)!; inputs.Drain();
        item.LegacyDataString = "1"; item.GroupId = 99;
        Assert.Same(first, inputs.Read(item.Id)); Assert.Empty(inputs.Drain());
        item.GetZ = 1; var raised = inputs.Read(item.Id)!;
        Assert.NotSame(first, raised); Assert.Same(first.Footprint, raised.Footprint); inputs.Drain();
        item.GetZ = 1; inputs.PublishCurrent(item);
        Assert.Same(raised, inputs.Read(item.Id)); Assert.Empty(inputs.Drain());
        item.Definition.InteractionType = Plus.HabboHotel.Items.InteractionType.Gate;
        inputs.PublishCurrent(item); inputs.Drain(); item.LegacyDataString = "0";
        Assert.NotEmpty(inputs.Drain()); Assert.Equal("0", inputs.Read(item.Id)!.State);
    }

    [Fact]
    public void ExtraDataCallbacksRunAfterNavigationPublicationAndOutsideItsLock()
    {
        var item = NavTest.Item(); item.Definition.InteractionType = Plus.HabboHotel.Items.InteractionType.Gate;
        var data = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat { Data = "0" }; item.ExtraData = data;
        var inputs = new NavInputs(2, 2); inputs.Attach(item);
        var notified = false;
        data.DataUpdated += (_, _) =>
        {
            Assert.False(Monitor.IsEntered(item.NavSync));
            Assert.Equal("1", inputs.Read(item.Id)!.State); notified = true;
        };
        item.LegacyDataString = "1"; Assert.True(notified);
    }

    [Fact]
    public void WaitingStateSetterHandlesDestroyBeforeAcquiringItsLock()
    {
        var item = NavTest.Item(); item.Definition.InteractionType = Plus.HabboHotel.Items.InteractionType.Gate;
        item.ExtraData = new Plus.HabboHotel.Items.DataFormat.LegacyDataFormat();
        var inputs = new NavInputs(2, 2); inputs.Attach(item);
        using var started = new ManualResetEventSlim();
        Exception? error = null;
        var writer = new Thread(() => { started.Set(); try { item.LegacyDataString = "1"; } catch (Exception e) { error = e; } });
        lock (item.NavSync)
        {
            writer.Start(); Assert.True(started.Wait(5000));
            Assert.True(SpinWait.SpinUntil(() => (writer.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000));
            item.Destroy();
        }
        Assert.True(writer.Join(5000)); Assert.Null(error); Assert.True(inputs.Read(item.Id)!.Removed);
    }

    [Fact]
    public async Task AdmissionAndConcurrentPickupCannotPublishAfterTheTombstone()
    {
        var fixture = Plus.Tests.Performance.RoomPerformanceFixture.Create(0, 0);
        var handler = fixture.Room.GetRoomItemHandler(); fixture.Map.GenerateMaps(); var navigation = NavTest.Enable(fixture.Map);
        var remove = handler.GetType().GetMethod("RemoveRoomItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var item = NavTest.Item(); item.GetX = item.GetY = 2;
        Task admission; Task pickup;
        lock (item.NavSync)
        {
            admission = Task.Run(() => Assert.True(handler.AdmitFloorItem(item)));
            pickup = Task.Run(() =>
            {
                Assert.True(SpinWait.SpinUntil(() => handler.GetItem(item.Id) != null, 5000));
                remove.Invoke(handler, [item]);
            });
        }
        await Task.WhenAll(admission, pickup).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Null(handler.GetItem(item.Id)); Assert.True(navigation.Inputs.Read(item.Id)!.Removed);
        item.GetZ = 99; Assert.True(navigation.Inputs.Read(item.Id)!.Removed);
    }

    [Fact]
    public void AdmissionEnablesSynchronizationBeforeExposureAndPlacementSerializesWithAttach()
    {
        var fixture = Plus.Tests.Performance.RoomPerformanceFixture.Create(0, 0);
        var handler = fixture.Room.GetRoomItemHandler();
        var navigation = NavTest.Enable(fixture.Map);
        var item = NavTest.Item(width: 2);
        using var admitted = new ManualResetEventSlim(); using var placed = new ManualResetEventSlim();
        Exception? admissionError = null; Exception? placementError = null;
        var admission = new Thread(() =>
        {
            try { Assert.True(handler.AdmitFloorItem(item)); }
            catch (Exception e) { admissionError = e; }
            finally { admitted.Set(); }
        });
        var placement = new Thread(() =>
        {
            try { item.SetPlacementState(2, 2, 1.5004,
                Plus.HabboHotel.Rooms.Gamemap.GetAffectedTiles(1, 2, 2, 2, 2), 2); }
            catch (Exception e) { placementError = e; }
            finally { placed.Set(); }
        });
        lock (item.NavSync)
        {
            admission.Start();
            Assert.True(SpinWait.SpinUntil(() => (admission.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000));
            Assert.Null(handler.GetItem(item.Id)); Assert.Null(item.NavigationInputs);
            placement.Start();
            Assert.True(SpinWait.SpinUntil(() => placed.IsSet || (placement.ThreadState & ThreadState.WaitSleepJoin) != 0, 5000));
            // Admission has enabled locking, although the attachment pointer is still null.
            Assert.False(placed.IsSet); Assert.Equal((0, 0, 0.0, 0), (item.GetX, item.GetY, item.GetZ, item.Rotation));
            navigation.Inputs.Attach(item); navigation.Compiler.ApplyNow();
            Assert.Equal(new[] { 0, 1 }, navigation.Inputs.AppliedRecords[item.Id].Footprint);
        }
        Assert.True(admission.Join(5000)); Assert.True(placement.Join(5000));
        Assert.True(admitted.IsSet); Assert.True(placed.IsSet); Assert.Null(admissionError); Assert.Null(placementError);
        navigation.Compiler.ApplyNow();
        Assert.Equal((2, 2, 1.5004, 2), (item.GetX, item.GetY, item.GetZ, item.Rotation));
        Assert.Equal(new[] { 10, 14 }, navigation.Inputs.AppliedRecords[item.Id].Footprint);
        Assert.Equal(1.5004, navigation.Grid.WalkZ[10]); Assert.Equal(1.5004, navigation.Grid.WalkZ[14]);
    }

}
