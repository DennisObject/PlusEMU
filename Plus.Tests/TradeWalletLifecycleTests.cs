using System.Reflection;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Users;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Xunit;

namespace Plus.Tests;

[Collection("Trade game fixture")]
public class TradeWalletLifecycleTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ATradeThatWaitsForAWalletLockNeverCommitsOverAWalletTheSessionClosedMeanwhile(int closingUser)
    {
        using var fixture = new TradeConfirmationServiceTests.TradeFixture(TestRoomSettings.Empty);
        var alice = fixture.Join(1, 7);
        var bob = fixture.Join(2, 8);
        var item = new InventoryItem
        {
            Id = 101,
            OwnerId = 1,
            Definition = new ItemDefinition { Id = 900, Type = ItemType.Floor, SpriteId = 11, PublicName = "Chair", AllowTrade = true }
        };
        Assert.True(alice.Habbo.Inventory.Furniture.AddItem(item));
        alice.Habbo.Credits = 100;
        bob.Habbo.Credits = 200;
        var trade = fixture.Start(alice, bob);
        trade.Users[0].OfferedItems.Add(item.Id, item);
        var closing = closingUser == 1 ? alice.Habbo : bob.Habbo;

        // the wallet of the user with the lower id is the first one the trade locks: hold it, let the trade queue up behind it, and close the session while it waits (what a disconnect does)
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            lock (alice.Habbo.WalletSync) {
                lock (bob.Habbo.WalletSync) {
                    held.Set();
                    release.Wait();
                    typeof(Habbo).GetField("_disconnected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(closing, true);
                }
            }
        });
        holder.Start();
        Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
        var finisher = new Thread(trade.Finish);
        finisher.Start();
        SpinWait.SpinUntil(() => (finisher.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5));
        release.Set();
        Assert.True(holder.Join(TimeSpan.FromSeconds(5)));
        Assert.True(finisher.Join(TimeSpan.FromSeconds(5)));

        Assert.Equal(0, fixture.Store.CommitCalls);
        Assert.Empty(fixture.Store.Logged);
        Assert.Same(item, alice.Habbo.Inventory.Furniture.GetItem(item.Id));
        Assert.Null(bob.Habbo.Inventory.Furniture.GetItem(item.Id));
        Assert.Equal((100, 200), (alice.Habbo.Credits, bob.Habbo.Credits));
    }
}
