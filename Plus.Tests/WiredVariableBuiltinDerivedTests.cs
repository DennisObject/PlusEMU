using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Plus.Database;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableBuiltinDerivedTests
{
    [Fact]
    public void NumericBuiltinEchoDerivedCatalogScalarAndSnapshotAgreeAndRecheckAuthority()
    {
        var fixture = new Fixture("@achievement_score");
        var level = fixture.AddMetadata("wf_xtra_var_lvlup_system");
        var id = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 20, 0, false)!.Value;
        var derived = new WiredVariableReference(WiredVariableTarget.User, $"custom:{id}");
        Assert.Contains(fixture.Variables.Catalog().Variables, variable => variable.Definition.ItemId == id && variable.ReadOnly);
        Assert.Equal(150, fixture.Read(new(WiredVariableTarget.User, "custom:20"))!.Value);
        Assert.Equal(2, fixture.Read(derived)!.Value);

        using (var reads = fixture.Module.CaptureReads([derived], fixture.Frame)) {
            Assert.Equal(2, reads.Read(derived, fixture.Holder, fixture.Frame)!.Value);
        }

        Assert.False(fixture.Module.Mutate(derived, fixture.Holder, WiredVariableMutation.Set, 9, fixture.Frame));
        fixture.Builtin.Value = null;
        Assert.Null(fixture.Read(derived));

        using (var reads = fixture.Module.CaptureReads([derived], fixture.Frame)) {
            Assert.Null(reads.Read(derived, fixture.Holder, fixture.Frame));
        }

        fixture.Builtin.Value = new(150, null, null);
        fixture.Directory.Owner = 6;
        Assert.Null(fixture.Read(derived));
        Assert.Empty(fixture.Variables.Catalog().Variables);
        fixture.Directory.Owner = 5;
        level.Item.SetState(1, 0, 0, []);
        Assert.Null(fixture.Read(derived));
    }

    [Fact]
    public void PresenceOnlyBuiltinEchoDoesNotBecomeNumericOrAcquireTimestamps()
    {
        var fixture = new Fixture("@is_idle");
        fixture.Builtin.Value = new(1, DateTimeOffset.FromUnixTimeMilliseconds(1000), DateTimeOffset.FromUnixTimeMilliseconds(2000));
        fixture.AddMetadata("wf_xtra_var_lvlup_system");
        var time = fixture.AddMetadata("wf_xtra_var_time_util");
        time.ApplyConfiguration(new() { IntParams = [1 << 21, 1] });
        var catalog = fixture.Variables.Catalog();
        var alias = Assert.Single(catalog.Variables);
        Assert.False(alias.HasValue);
        Assert.False(alias.CanReadTimestamps);
        Assert.NotNull(fixture.Read(new(WiredVariableTarget.User, "custom:20")));

        foreach (var id in new[] { WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 20, 0, false)!.Value,
            WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 20, 21, true)!.Value }) {
            var reference = new WiredVariableReference(WiredVariableTarget.User, $"custom:{id}");
            Assert.Null(fixture.Read(reference));
            using var reads = fixture.Module.CaptureReads([reference], fixture.Frame);
            Assert.Null(reads.Read(reference, fixture.Holder, fixture.Frame));
        }

        fixture.Builtin.Value = null;
        Assert.Null(fixture.Read(new(WiredVariableTarget.User, "custom:20")));
        fixture.Directory.StoredPresence = true;
        Assert.True(fixture.Module.Mutate(new(WiredVariableTarget.User, "custom:20"), fixture.Holder, WiredVariableMutation.Give, 0, fixture.Frame));
        var storedTimeId = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 20, 21, true)!.Value;
        Assert.Contains(fixture.Variables.Catalog().Variables, variable => variable.Definition.ItemId == storedTimeId);
        Assert.Equal(1, fixture.Read(new(WiredVariableTarget.User, $"custom:{storedTimeId}"))!.Value);
    }

    [Fact]
    public void NumericBuiltinTimeValueConvertsButIncidentalProviderTimestampsStayUnavailable()
    {
        var fixture = new Fixture("@achievement_score");
        fixture.Builtin.Value = new(150, DateTimeOffset.FromUnixTimeMilliseconds(12000), DateTimeOffset.FromUnixTimeMilliseconds(24000));
        var time = fixture.AddMetadata("wf_xtra_var_time_util");
        time.ApplyConfiguration(new() { IntParams = [1 << 22, 0] });
        var id = WiredRoomVariables.SyntheticId(WiredVariableTarget.User, 20, 22, true)!.Value;
        var reference = new WiredVariableReference(WiredVariableTarget.User, $"custom:{id}");
        Assert.Equal(2, fixture.Read(reference)!.Value);

        using (var reads = fixture.Module.CaptureReads([reference], fixture.Frame)) {
            Assert.Equal(2, reads.Read(reference, fixture.Holder, fixture.Frame)!.Value);
        }

        foreach (var mode in new[] { 1, 2 }) {
            time.ApplyConfiguration(new() { IntParams = [1 << 22, mode] });
            Assert.DoesNotContain(fixture.Variables.Catalog().Variables, variable => variable.Definition.ItemId == id);
            Assert.Null(fixture.Read(reference));
            using var reads = fixture.Module.CaptureReads([reference], fixture.Frame);
            Assert.Null(reads.Read(reference, fixture.Holder, fixture.Frame));
        }
    }

    private sealed class Fixture
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly ConcurrentDictionary<uint, Item> _floor;
        private uint _nextMetadata = 1000;
        public Authority Directory { get; }
        public BuiltinValues Builtin { get; } = new();
        public WiredRoomVariables Variables { get; }
        public WiredVariableModule Module { get; }
        public WiredVariableHolder Holder { get; } = new(WiredVariableTarget.User, 7, 1);
        public WiredVariableFrame Frame { get; }
        public Fixture(string key)
        {
            var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
            room.Id = 1;
            room.OwnerId = 5;
            var handler = new RoomItemHandling(room, TestRoomItemStore.Instance, TestRoomItemMetadataStore.Instance, TestGameClientManager.Empty, TestLanguageManager.RoomItems, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
            typeof(Room).GetField("_roomItemHandling", Private)!.SetValue(room, handler);
            _floor = (ConcurrentDictionary<uint, Item>)typeof(RoomItemHandling).GetField("_floorItems", Private)!.GetValue(handler)!;
            var database = DispatchProxy.Create<IDatabase, ModernWiredRuntimeTests.RecordingProxy>();
            ((ModernWiredRuntimeTests.RecordingProxy)(object)database).InvokeMethod = (method, _) => throw new InvalidOperationException("Unexpected database access: " + method.Name);
            Variables = new(room, database, new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
            Directory = new(key);
            var resolve = typeof(WiredRoomVariables).GetMethod("ResolveDerived", Private)!.CreateDelegate<Func<WiredVariableReference, WiredVariableDerivation?>>(Variables);
            Module = new(1, Directory, new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)), Builtin, resolve);
            typeof(WiredRoomVariables).GetField("<Module>k__BackingField", Private)!.SetValue(Variables, Module);
            Frame = new(1, [Holder]);
            var item = new Item { Id = 20, OwnerId = 5, Definition = new() { InteractionName = "wf_var_echo" } };
            _floor[item.Id] = item;
            var box = Variables.CreateBox(item)!;
            Assert.True(box.TryValidateConfiguration(new() { Text = "{\"variableName\":\"points\",\"sourceTargetType\":0,\"sourceVariableToken\":\"internal:" + key + "\"}" }, out var config, out _));
            box.ApplyConfiguration(config);
            Variables.ConfigurationLoaded(box);
        }
        public WiredVariableMetadataBox AddMetadata(string name)
        {
            var item = new Item { Id = _nextMetadata++, Definition = new() { InteractionName = name } };
            _floor[item.Id] = item;
            var box = Assert.IsType<WiredVariableMetadataBox>(Variables.CreateBox(item));
            Variables.ConfigurationLoaded(box);

            return box;
        }
        public WiredVariableValue? Read(WiredVariableReference reference) => Module.Read(reference, Holder, Frame);
    }
    private sealed class Authority(string key) : IWiredVariableDirectory
    {
        public uint Owner = 5;
        public bool StoredPresence;
        public uint? GetRoomOwner(uint id) => id == 1 ? Owner : null;
        public WiredVariableDefinition? Find(uint id) => id == 20 ? new(20, 1, 5, "points", WiredVariableTarget.User,
            WiredVariableAvailability.RoomActive, !StoredPresence, Link: StoredPresence ? null : new(1, new(WiredVariableTarget.User, "internal:" + key), false)) : null;
    }
    private sealed class BuiltinValues : IWiredBuiltinVariables
    {
        public WiredVariableValue? Value = new(150, null, null);
        public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) => Value;
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame) => false;
    }
}
