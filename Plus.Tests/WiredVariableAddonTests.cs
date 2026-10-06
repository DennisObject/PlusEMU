using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableAddonTests
{
    [Fact]
    public void VariableFilterRanksSelectedHoldersAndUsesSnapshotReferenceAmount()
    {
        var (room, context, module, holders) = World();
        var box = Box("wf_xtra_filter_users_by_var", room, module);
        var config = new WiredConfiguration { IntParams = [0, 1, 0, 0, 200, 0], Text = "custom:10\tcustom:11" };
        Assert.True(box.TryValidateConfiguration(config, out var valid, out _));
        box.ApplyConfiguration(valid);
        Assert.True(box.Apply(context));
        Assert.Equal(new[] { 2, 3 }, context.Selected.UserIds.Order().ToArray());
        var empty = config with { IntParams = [0, 0, 0, 0, 0, 0] };
        box.ApplyConfiguration(empty);
        Assert.True(box.Apply(context));
        Assert.Empty(context.Selected.UserIds);
        Assert.Empty(module.DrainChanges());
    }
    [Fact]
    public void TextFormatterUsesCapturedSettingsCurrentValuesAndTextConnector()
    {
        var (room, context, module, holders) = World();
        var box = Box("wf_xtra_text_output_variable", room, module);
        var config = new WiredConfiguration { IntParams = [0, 2, 2, 200, 0], Text = "custom:10\tpoints\t|" };
        Assert.True(box.TryValidateConfiguration(config, out var valid, out _));
        box.ApplyConfiguration(valid);
        Assert.True(box.Apply(context));
        Assert.Equal("scores=ten|twenty|30", context.Policy.FormatText(context, "scores=$(points)"));
        box.ApplyConfiguration(config with { Text = "custom:11\tpoints\t," });
        module.Mutate(new(WiredVariableTarget.User, "custom:10"), holders[0], WiredVariableMutation.Set, 40, context.VariableFrame!);
        Assert.Equal("scores=40|twenty|30", context.Policy.FormatText(context, "scores=$(points)"));
    }
    private static WiredVariableAddonBox Box(string name, Room room, WiredVariableModule module)
    {
        Assert.True(WiredBoxRegistry.TryGet(name, out var descriptor));

        return new(room, new Item { Id = 90 }, descriptor, module, _ => new Dictionary<int, string> { [10] = "ten", [20] = "twenty" });
    }
    private static (Room Room, WiredRuntimeContext Context, WiredVariableModule Module, WiredVariableHolder[] Holders) World()
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room));
        room.Id = 1;
        var users = Enumerable.Range(1, 3).Select(i => new RoomUser(900 + i, 0, i, room, null, TestChatEmotions.Unused, TestRewardProgress.Unused)).ToArray();
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Enter), new(() => [], () => users), new Operations());
        context.SelectorPool.UserIds.UnionWith(users.Select(x => x.VirtualId));
        context.Selected.UserIds.UnionWith(users.Select(x => x.VirtualId));
        var frame = WiredVariableRuntimeFrames.Create(context);
        context.VariableFrame = frame;
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        var holders = frame.Holders.ToArray();

        for (var i = 0; i < holders.Length; i++) {
            module.Mutate(new(WiredVariableTarget.User, "custom:10"), holders[i], WiredVariableMutation.Give, (i + 1) * 10, frame);
            module.Mutate(new(WiredVariableTarget.User, "custom:11"), holders[i], WiredVariableMutation.Give, 2, frame);
        }

        module.DrainChanges();

        return (room, context, module, holders);
    }
    private sealed class Directory : IWiredVariableDirectory
    {
        public uint? GetRoomOwner(uint roomId) => 5;
        public WiredVariableDefinition? Find(uint id) => id is 10 or 11 ? new(id, 1, 5, "v" + id, WiredVariableTarget.User, WiredVariableAvailability.RoomActive, true) : null;
    }
    private sealed class Operations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
