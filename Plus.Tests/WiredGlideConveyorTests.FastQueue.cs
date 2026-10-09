using System.Text.Json;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public sealed partial class WiredGlideConveyorTests
{
    // Sept9 own-room guide replay: eight BC tiles, seven moving picks returned in descending ID order.
    // Both directions are verified natively; mirrored creation order guards against ID-dependent recapture.
    [Theory]
    [InlineData(2, 4, 5, false)]
    [InlineData(2, 4, 5, true)]
    [InlineData(6, 11, 10, false)]
    [InlineData(6, 11, 10, true)]
    public void FastQueueDescendingPicksCarryARiderOneTileInTheFirstPulse(int direction, int startX, int expectedX, bool reverseCreation)
    {
        var layout = FastQueueLayout(direction, reverseCreation);
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, startX, 10, startX, 11);
        var moving = layout.Where(item => item.Name == "bc_tile_1" && item.X != (direction == 2 ? 11 : 4)).Select(item => item.Id);
        var saved = JsonSerializer.Deserialize<WiredConfiguration>(layout.Single(item => item.Id == 12).Json!)!;

        Assert.Equal(moving.Reverse(), saved.SelectedItems);
        Assert.Equal((startX, 11, 1.0), (rider.X, rider.Y, rider.Z));

        f.Advance(250);

        Assert.Equal((expectedX, 11, 1.0), (rider.X, rider.Y, rider.Z));
        Assert.Equal(layout.Where(item => item.Name == "bc_tile_1").Select(item => item.X), Enumerable.Range(1, 8).Select(id => f.Items[(uint)id].GetX));
        Assert.All(Enumerable.Range(1, 8), id => Assert.Equal(0, f.Items[(uint)id].GetZ));
    }

    [Theory]
    [InlineData(2, 4, 1)]
    [InlineData(6, 11, -1)]
    public void FastQueueCarriesOneTilePerPulseUntilTheFixedEnd(int direction, int startX, int step)
    {
        var f = new Fixture(FastQueueLayout(direction), live: true);
        var rider = f.Walker(1, startX, 10, startX, 11);

        f.Advance(250);
        Assert.Equal(startX + step, rider.X);

        for (var pulse = 2; pulse <= 9; pulse++) {
            f.Advance(200);
            Assert.Equal(startX + Math.Min(pulse, 7) * step, rider.X);
            Assert.Equal((11, 1.0), (rider.Y, rider.Z));
        }
    }

    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 4)]
    public void FastQueueWithoutResetCarriesOnlyWhenTheAddonIsPresent(bool carry, int expectedX)
    {
        var layout = FastQueueLayout(2).Where(item => item.Id != 10 && (carry || item.Id != 17)).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);

        f.Advance(250);

        Assert.Equal((expectedX, 11, carry ? 1.0 : 0.0), (rider.X, rider.Y, rider.Z));
        Assert.Equal(Enumerable.Range(5, 7), Enumerable.Range(1, 7).Select(id => f.Items[(uint)id].GetX));
        Assert.Equal(11, f.Items[8].GetX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastQueueStackedMoversCarryTheSamePassengerOnlyOnce(bool together)
    {
        var layout = Configure(FastQueueLayout(2).Where(item => item.Id != 10).ToArray(), (17, [1, 900]), (16, [1, 1, 1, 0, 900, 900, 900])).ToList();
        var direction = layout.FindIndex(item => item.Id == 12);
        var saved = JsonSerializer.Deserialize<WiredConfiguration>(layout[direction].Json!)!;
        layout[direction] = layout[direction] with
        {
            Name = together ? "wf_act_rel_mov" : layout[direction].Name,
            Json = JsonSerializer.Serialize(saved with
            {
                IntParams = together ? [1, 1, 1, 0, 100] : [2, 0, 100, 0],
                SelectedItems = [19, .. saved.SelectedItems]
            })
        };
        layout.Add((19, 4, 11, 1, 0, "bc_tile_1", null));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        Assert.Equal(2.0, rider.Z);

        f.Advance(250);

        Assert.Equal((5, 11), (rider.X, rider.Y));
        Assert.Equal(5, f.Items[1].GetX);
        Assert.Equal(5, f.Items[19].GetX);
    }

    [Fact]
    public void FastQueueBlockedMoverDoesNotConsumeThePassengersLaterSuccessfulCarry()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 11, 1, 0, "bc_tile_1", null));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(true, new HashSet<int> { rider.VirtualId });
        var movement = new WiredRoomMovement((_, _, _) => { });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1], f.Items[19]]);

        f.Owned(() =>
        {
            Assert.False(movement.MoveFurniture(context, f.Items[1], 0, 11, 0, null, passengers: passengers));
            Assert.True(movement.MoveFurniture(context, f.Items[19], 5, 11, 0, null, passengers: passengers));
        });

        Assert.Equal((5, 11), (rider.X, rider.Y));
        Assert.Equal(4, f.Items[1].GetX);
        Assert.Equal(5, f.Items[19].GetX);
    }

    [Fact]
    public void FastQueueCapturedPassengerCannotMutateAReusedUserIdentity()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(false, new HashSet<int> { rider.VirtualId });
        var movement = new WiredRoomMovement((_, _, _) => { });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1]]);
        var replacement = new RoomUser(99, context.Room.Id, rider.VirtualId, context.Room, null,
            TestChatEmotions.Unused, TestRewardProgress.Unused)
        {
            X = 4,
            Y = 11,
            Z = 1
        };
        f.ReplaceUser(replacement);

        f.Owned(() => Assert.True(movement.MoveFurniture(context, f.Items[1], 5, 11, 0, null, passengers: passengers)));

        Assert.Equal((4, 11, 1.0), (rider.X, rider.Y, rider.Z));
        Assert.Equal((4, 11, 1.0), (replacement.X, replacement.Y, replacement.Z));
    }

    [Fact]
    public void FastQueueQueuedFailedCarryAllowsTheLaterCandidateToSucceed()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 11, 1, 0, "bc_tile_1", null));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(true, new HashSet<int> { rider.VirtualId });
        context.Policy.Addons.Physics = new(true, f.Items.Keys.ToHashSet(), new HashSet<int>(), new HashSet<uint> { 8 });
        var movement = new WiredRoomMovement((_, _, _) => { });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1], f.Items[19]]);
        var navigation = context.Room.GetGameMap().Navigation!;
        var placed = new List<bool>();
        var collision = new WiredCollisionPolicy(f.Items.Keys.ToHashSet(), new HashSet<int>(), new HashSet<uint>());

        navigation.RunOwner(rider, (_, _) => placed.Add(WiredRoomOperations.MoveItem(context.Room, f.Items[8], 5, 11, 0, 0,
            animate: false, collision: collision)));
        Assert.True(movement.MoveFurniture(context, f.Items[1], 5, 11, 0, null, passengers: passengers));
        navigation.RunOwner(rider, (_, _) => placed.Add(WiredRoomOperations.MoveItem(context.Room, f.Items[8], 11, 11, 0, 0,
            animate: false, collision: collision)));
        Assert.True(movement.MoveFurniture(context, f.Items[19], 6, 11, 0, null, passengers: passengers));
        Assert.Equal((4, 11), (rider.X, rider.Y));

        f.DrainMovement();

        Assert.Equal(new[] { true, true }, placed);
        Assert.Equal((6, 11), (rider.X, rider.Y));
    }

    [Fact]
    public void FastQueueSeparateActionsCanCarryThePassengerAgainInTheSameFiring()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 12, 1, 0, "wf_act_move_to_dir", JsonSerializer.Serialize(new WiredConfiguration
        {
            IntParams = [2, 0, 100, 1],
            SelectedItems = [1]
        })));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);

        f.Advance(250);

        Assert.Equal((6, 11), (rider.X, rider.Y));
        Assert.Equal(Enumerable.Range(4, 8), Enumerable.Range(1, 8).Select(id => f.Items[(uint)id].GetX));
    }

    [Fact]
    public void FastQueueQueuedOverlappingMoversShareOneSuccessfulCarry()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 11, 1, 0, "bc_tile_1", null));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(true, new HashSet<int> { rider.VirtualId });
        var movement = new WiredRoomMovement((_, _, _) => { });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1], f.Items[19]]);
        Assert.True(movement.MoveFurniture(context, f.Items[1], 5, 11, 0, null, passengers: passengers));
        Assert.True(movement.MoveFurniture(context, f.Items[19], 6, 11, 0, null, passengers: passengers));
        Assert.Equal((4, 11), (rider.X, rider.Y));

        f.DrainMovement();

        Assert.Equal((5, 11), (rider.X, rider.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastQueueQueuedCarryRejectsAnInterveningRelocationOrReusedIdentity(bool reuse)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(false, new HashSet<int> { rider.VirtualId });
        var movement = new WiredRoomMovement((_, _, _) => { });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1]]);
        RoomUser? replacement = null;
        context.Room.GetGameMap().Navigation!.RunOwner(rider, (_, _) =>
        {
            if (reuse) {
                replacement = new(99, context.Room.Id, rider.VirtualId, context.Room, null,
                    TestChatEmotions.Unused, TestRewardProgress.Unused)
                {
                    X = 4,
                    Y = 11,
                    Z = 1
                };
                f.ReplaceUser(replacement);
            }
            else {
                rider.SetPos(8, 10, 0);
            }
        });
        Assert.True(movement.MoveFurniture(context, f.Items[1], 5, 11, 0, null, passengers: passengers));

        f.DrainMovement();

        Assert.Equal(reuse ? (4, 11) : (8, 10), (rider.X, rider.Y));

        if (replacement != null) {
            Assert.Equal((4, 11), (replacement.X, replacement.Y));
        }
    }

    [Fact]
    public void FastQueueSpeechAdmissionQueuesOneCarryUntilTheRoomOwnerDrains()
    {
        var layout = FastQueueLayout(2).Select(item => item.Id == 11
            ? item with
            {
                Name = "wf_trg_says_something",
                Json = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0], Text = "pulse" })
            } : item).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        Assert.False(RoomOwnerScope.IsOwner(f.MovementContext().Room));

        rider.OnChat(0, "pulse", false);
        Assert.Equal((4, 11), (rider.X, rider.Y));
        f.DrainMovement();

        Assert.Equal((5, 11), (rider.X, rider.Y));
        Assert.Equal(Enumerable.Range(4, 8), Enumerable.Range(1, 8).Select(id => f.Items[(uint)id].GetX));
    }

    private static (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] FastQueueLayout(int direction, bool reverseCreation = false)
    {
        int X(uint id) => reverseCreation ? 12 - (int)id : (int)id + 3;
        var picks = Enumerable.Range(1, 8).Select(id => (uint)id)
            .Where(id => X(id) != (direction == 2 ? 11 : 4)).Reverse().ToArray();
        var snapshots = picks.Select(id => new WiredFurniSnapshot(id, 29284, X(id), 11, 0, 0, "")).ToArray();
        var layout = Enumerable.Range(1, 8).Select(id =>
            ((uint)id, X((uint)id), 11, 0.0, 0, "bc_tile_1", (string?)null)).ToList();

        string Config(int[] parameters, uint[]? selected = null, WiredFurniSnapshot[]? captures = null) =>
            JsonSerializer.Serialize(new WiredConfiguration
            {
                IntParams = [.. parameters],
                SelectedItems = selected == null ? [] : [.. selected],
                Snapshots = captures == null ? [] : [.. captures]
            });

        layout.AddRange([
            (9, 8, 13, 0, 0, "wf_antenna2", null),
            (10, 6, 12, 0, 0, "wf_trg_recv_signal", Config([0, 100], [9])),
            (11, 4, 12, 0, 0, "wf_trg_period_short", Config([4])),
            (12, 4, 12, 0.65, 0, "wf_act_move_to_dir", Config([direction, 0, 100, 1], picks)),
            (13, 4, 12, 1.3, 0, "wf_act_send_signal", Config([0, 200, 200, 0, 0, 0], [9])),
            (14, 6, 12, 0.65, 0, "wf_act_match_to_sshot", Config([0, 0, 1, 1, 100], picks, snapshots)),
            (15, 6, 12, 1.3, 0, "wf_xtra_mov_physics", Config([0, 1, 1, 0, 900, 900, 900])),
            (16, 4, 12, 1.95, 0, "wf_xtra_mov_physics", Config([1, 0, 0, 0, 900, 900, 900])),
            (17, 4, 12, 2.32, 0, "wf_xtra_mov_carry_users", Config([0, 900])),
            (18, 4, 12, 2.69, 0, "wf_xtra_anim_time", Config([200]))
        ]);

        return layout.ToArray();
    }
}
