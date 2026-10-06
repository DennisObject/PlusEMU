using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;
using static Plus.Tests.WiredSelectorTests;

namespace Plus.Tests;

public sealed class WiredSelectorVariableBridgeTests
{
    [Fact]
    public void SelectorsUseRoomIdsButReadStablePlayerAndFurnitureHoldersAndDisposeTheirSnapshot()
    {
        var user = new WiredVariableHolder(WiredVariableTarget.User, 999, 4);
        var furniture = new WiredVariableHolder(WiredVariableTarget.Furni, 3, 3);
        var frame = new WiredVariableFrame(1, [user, furniture]);
        var module = new WiredVariableModule(1, new Directory(), new MemoryWiredVariableStore(), new FixedTimeProvider(DateTimeOffset.FromUnixTimeMilliseconds(1000)));
        module.Mutate(new(user.Target, "custom:10"), user, WiredVariableMutation.Give, 5, frame);
        module.Mutate(new(furniture.Target, "custom:11"), furniture, WiredVariableMutation.Give, 8, frame);
        var queries = WiredSelectorVariableBridge.Create(module, frame);
        var inputs = Inputs() with
        {
            FurniVariablePredicate = queries.FurniPredicate,
            UserVariablePredicate = queries.UserPredicate
        };
        var selected = WiredSelectorModule.SelectRaw("wf_slc_users_with_var", Config(text: "custom:10"), World(), inputs);
        Assert.Equal(new[] { 4 }, selected.Selection.UserIds);
        Assert.False(queries.UserPredicate("wf_slc_users_with_var", Config([0, 2, 0, 0, 0, 0, 0, 0, 0], text: "custom:10"), 999));
        Assert.Equal(new uint[] { 3 }, WiredSelectorModule.SelectRaw("wf_slc_furni_with_var", Config(text: "custom:11"), World(), inputs).Selection.FurniIds);
        Assert.Equal(8, queries.ReadOperand(new(1, "custom:11", 0, 100, Config(picks: [3]))));
        queries.Dispose();
        Assert.Throws<ObjectDisposedException>(() => queries.ReadOperand(new(1, "custom:11", 0, 100, Config(picks: [3]))));
    }

    private sealed class Directory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint itemId) => itemId is 10 or 11
            ? new(itemId, 1, 5, "test", itemId == 10 ? WiredVariableTarget.User : WiredVariableTarget.Furni,
                WiredVariableAvailability.RoomActive, true) : null;
        public uint? GetRoomOwner(uint roomId) => 5;
    }
}
