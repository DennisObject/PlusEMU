using System.Runtime.CompilerServices;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed class WiredVariableOperandTests
{
    [Theory]
    [InlineData(WiredVariableTarget.User)]
    [InlineData(WiredVariableTarget.Furni)]
    public void TypedExecutorAssignsAndComparesEachHoldersOwnReference(WiredVariableTarget target)
    {
        var room = (Room)RuntimeHelpers.GetUninitializedObject(typeof(Room)); room.Id = 1; room.OwnerId = 5;
        var items = new[] { new Item { Id = 301, OwnerId = 5 }, new Item { Id = 302, OwnerId = 5 } };
        var users = new[] { new RoomUser(901, 1, 1, room), new RoomUser(902, 1, 2, room) };
        var context = new WiredRuntimeContext(room, new(WiredEventKind.Enter), new(() => items, () => users), new UnusedOperations());
        context.SelectorPool.FurniIds.UnionWith(items.Select(x => x.Id)); context.SelectorPool.UserIds.UnionWith(users.Select(x => x.VirtualId));
        var frame = WiredVariableRuntimeFrames.Create(context);
        var holders = frame.Holders.Where(x => x.Target == target).ToArray();
        var directory = new Directory(target, target); var module = new WiredVariableModule(1, directory, new MemoryWiredVariableStore(), () => 1000);
        for (var i = 0; i < holders.Length; i++)
        {
            module.Mutate(new(target, "custom:10"), holders[i], WiredVariableMutation.Give, 0, frame);
            module.Mutate(new(target, "custom:11"), holders[i], WiredVariableMutation.Give, (i + 1) * 10, frame);
        }
        var box = Assert.IsType<WiredVariableConfiguredBox>(WiredVariableBoxFactory.Create(room,
            new Item { Id = 50, Definition = new() { InteractionName = "wf_act_change_var_val" } }, module, () => 2000));
        var configuration = Config(target, target);
        Assert.True(box.TryValidateConfiguration(configuration, out var validated, out _)); box.ApplyConfiguration(validated);
        Assert.True(box.Execute(context));
        Assert.Equal(new[] { 10, 20 }, holders.Select(x => module.Read(new(target, "custom:10"), x, frame)!.Value));
        var executor = new WiredVariableExecutors(module, () => 2000);
        Assert.True(executor.Execute("wf_cnd_var_val_match", configuration with { IntParams = [(int)target, 2, 1, 0, (int)target, 200, 200, 200, 200, 0] }, frame));
        Assert.True(executor.Execute("wf_cnd_var_val_match", configuration with { Text = "custom:10\tcustom:10\t", IntParams = [(int)target, 2, 1, 0, (int)target, 200, 200, 200, 200, 0] }, frame));
        Assert.False(executor.Execute("wf_act_change_var_val", configuration with { Text = "custom:10\tcustom:99\t" }, frame));
        module.DrainChanges();
        Assert.False(executor.Execute("wf_act_change_var_val", configuration with { Text = "custom:12\tcustom:11\t", IntParams = [(int)target, 1, 1, 0, (int)target, 200, 200, 200, 200] }, frame));
        Assert.Empty(module.DrainChanges());
        var reversed = new WiredVariableFrame(1, frame.Holders) { Signal = holders.Reverse().ToArray() };
        reversed.Selector.AddRange(holders);
        foreach (var holder in holders) module.Mutate(new(target, "custom:10"), holder, WiredVariableMutation.Set, 0, reversed);
        Assert.True(executor.Execute("wf_act_change_var_val", configuration with { IntParams = [(int)target, 0, 1, 0, (int)target, 200, 200, 201, 201] }, reversed));
        Assert.Equal(new[] { 10, 20 }, holders.Select(x => module.Read(new(target, "custom:10"), x, reversed)!.Value));
    }

    [Fact]
    public void DifferentTargetReferencesUsePositionThenFirstWhenDestinationsOutnumberOperands()
    {
        var users = Enumerable.Range(1, 3).Select(i => new WiredVariableHolder(WiredVariableTarget.User, 900 + i, i)).ToArray();
        var furniture = new[] { new WiredVariableHolder(WiredVariableTarget.Furni, 301, 301), new WiredVariableHolder(WiredVariableTarget.Furni, 302, 302) };
        var frame = new WiredVariableFrame(1, users.Concat(furniture).ToArray()); frame.Selector.AddRange(frame.Holders);
        var module = new WiredVariableModule(1, new Directory(WiredVariableTarget.User, WiredVariableTarget.Furni), new MemoryWiredVariableStore(), () => 1000);
        foreach (var user in users) module.Mutate(new(user.Target, "custom:10"), user, WiredVariableMutation.Give, 0, frame);
        for (var i = 0; i < furniture.Length; i++) module.Mutate(new(furniture[i].Target, "custom:11"), furniture[i], WiredVariableMutation.Give, (i + 1) * 10, frame);
        var executor = new WiredVariableExecutors(module, () => 2000);
        Assert.True(executor.Execute("wf_act_change_var_val", Config(WiredVariableTarget.User, WiredVariableTarget.Furni), frame));
        Assert.Equal(new[] { 10, 20, 10 }, users.Select(x => module.Read(new(x.Target, "custom:10"), x, frame)!.Value));
    }

    [Theory]
    [InlineData(0, 0, false, false, false, false)]
    [InlineData(2, 0, false, false, true, true)]
    [InlineData(2, 1, false, true, true, false)]
    [InlineData(2, 2, true, true, false, false)]
    public void PresenceNegatesAggregateAndEmptySelectionNeverMatches(int count, int present, bool positiveAll, bool positiveAny, bool negativeAll, bool negativeAny)
    {
        var holders = Enumerable.Range(1, count).Select(i => new WiredVariableHolder(WiredVariableTarget.User, 900 + i, i)).ToArray();
        var frame = new WiredVariableFrame(1, holders); frame.Selector.AddRange(holders);
        var module = new WiredVariableModule(1, new Directory(WiredVariableTarget.User, WiredVariableTarget.User), new MemoryWiredVariableStore(), () => 1000);
        foreach (var holder in holders.Take(present)) module.Mutate(new(holder.Target, "custom:10"), holder, WiredVariableMutation.Give, 1, frame);
        var executor = new WiredVariableExecutors(module, () => 2000);
        var all = new WiredConfiguration { IntParams = [0, 200, 0, 0], Text = "custom:10" }; var any = all with { IntParams = [0, 200, 0, 1] };
        Assert.Equal(positiveAll, executor.Execute("wf_cnd_has_var", all, frame)); Assert.Equal(positiveAny, executor.Execute("wf_cnd_has_var", any, frame));
        Assert.Equal(negativeAll, executor.Execute("wf_cnd_neg_has_var", all, frame)); Assert.Equal(negativeAny, executor.Execute("wf_cnd_neg_has_var", any, frame));
    }
    private static WiredConfiguration Config(WiredVariableTarget target, WiredVariableTarget reference) => new()
    { IntParams = [(int)target, 0, 1, 0, (int)reference, 200, 200, 200, 200], Text = "custom:10\tcustom:11\t" };
    private sealed class Directory(WiredVariableTarget target, WiredVariableTarget reference) : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => id is >= 10 and <= 12 ? new(id, 1, 5, $"v{id}", id == 11 ? reference : target, WiredVariableAvailability.RoomActive, true,
            Link: id == 12 ? new(1, new(target, "custom:10"), true) : null) : null;
        public uint? GetRoomOwner(uint roomId) => roomId == 1 ? 5u : null;
    }
    private sealed class UnusedOperations : IWiredRuntimeOperations
    {
        public bool CallStacks(WiredRuntimeContext context, IEnumerable<Item> targets, bool negative = false) => throw new NotSupportedException();
        public bool SendSignal(WiredRuntimeContext context, IEnumerable<Item> receivers, WiredSelection selection, bool negative = false) => throw new NotSupportedException();
        public void ResetTimers(IEnumerable<Item> targets) => throw new NotSupportedException();
    }
}
