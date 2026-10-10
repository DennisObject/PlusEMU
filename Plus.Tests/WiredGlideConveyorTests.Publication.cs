using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Addons;
using Plus.HabboHotel.Items.Wired.Modern.Selectors;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Rooms;
using Xunit;

namespace Plus.Tests;

public sealed partial class WiredGlideConveyorTests
{
    [Theory]
    [InlineData(-100)]
    [InlineData(0)]
    [InlineData(100)]
    public async Task CanonicalStrengthActualOwnerGuidePublishesSignedFloorAndCarryFields(int strength)
    {
        var layout = FastQueueLayout(2).ToList();
        var config = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [7, 100, strength, 0, 0, 0, 0] });
        layout.Add((19, 4, 12, 4, 0, "wf_xtra_mov_curve", config));
        layout.Add((20, 6, 12, 4, 0, "wf_xtra_mov_curve", config));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        room.Type = "private";
        room.OwnerName = rider.GetUsername();
        var packets = CapturePublication(rider);
        await new WiredUserVariablesRequestEvent(new WiredVariableMenuService()).Parse(room, rider.GetClient()!,
            new FlashIncomingPacket { Buffer = Array.Empty<byte>() });
        packets.Clear();

        f.Advance(250);

        var moves = ReadTrajectoryPublication(packets);
        var floor = moves.Where(move => move.Type == 1).ToArray();
        Assert.Equal(Enumerable.Range(1, 7), floor.Select(move => move.Id));
        Assert.All(floor, move =>
        {
            Assert.Equal(strength, move.Strength);
            Assert.Null(move.Overshoot);
            Assert.Equal(move.FromX, move.ToX);
            Assert.Equal(500, move.Duration);
        });
        var carried = Assert.Single(moves.Where(move => move.Type == 0));
        Assert.Equal(strength, carried.Strength);
        Assert.Equal((4, 5, "1", "1", 200), (carried.FromX, carried.ToX, carried.FromZ, carried.ToZ, carried.Duration));
        Assert.Equal((5, 11, 1.0), (rider.X, rider.Y, rider.Z));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData("literal-positive", 100, 100)]
    [InlineData("literal-negative", -100, -100)]
    [InlineData("literal-zero", 0, 0)]
    [InlineData("variable-positive-clamp", 1000, 1000)]
    [InlineData("variable-negative-clamp", -1000, -1000)]
    [InlineData("variable-zero", 0, 0)]
    [InlineData("missing-variable", null, null)]
    [InlineData("missing-variable-projectile", 100, null)]
    [InlineData("projectile-positive", 100, null)]
    [InlineData("projectile-negative", -100, null)]
    [InlineData("projectile-zero", null, null)]
    [InlineData("projectile-unselected", null, null)]
    [InlineData("addon-zero-projectile", 0, 0)]
    [InlineData("addon-negative-projectile", -100, -100)]
    [InlineData("absent", null, null)]
    public void CanonicalStrengthResolvedAddonAndProjectileScopeReachActualProducers(string scenario, int? floorExpected, int? avatarExpected)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        var packets = CapturePublication(rider);
        var world = new WiredSelectorWorld(12, 17, [WiredRoomMovement.Furniture(f.Items[1])], []);
        var reads = 0;
        long? variable = scenario switch
        {
            "variable-positive-clamp" => long.MaxValue,
            "variable-negative-clamp" => long.MinValue,
            "variable-zero" => 0,
            _ => null
        };
        var input = new WiredAddonInputs(world, new(new(), new(), new()), 0, request =>
        {
            reads++;
            Assert.Equal("user:99", request.Token);

            return variable;
        });
        var hasProjectile = scenario.Contains("projectile", StringComparison.Ordinal);

        if (hasProjectile) {
            var ints = new int[24];
            ints[18] = scenario == "projectile-negative" ? -100 : scenario == "projectile-zero" ? 0 : 100;
            Assert.True(new WiredAddonModule("wf_xtra_rotate_to_dir", new WiredConfiguration
            {
                IntParams = ints.ToImmutableArray(),
                SelectedItems = scenario == "projectile-unselected" ? [999] : [1]
            }).Apply(input, context.Policy.Addons));
        }

        var useVariable = scenario.StartsWith("variable", StringComparison.Ordinal) || scenario.StartsWith("missing", StringComparison.Ordinal);
        var hasAddon = !scenario.StartsWith("projectile", StringComparison.Ordinal) && scenario != "absent";

        if (hasAddon) {
            var strength = scenario.Contains("negative", StringComparison.Ordinal) ? -100 : scenario.Contains("zero", StringComparison.Ordinal) ? 0 : 100;
            Assert.True(new WiredAddonModule("wf_xtra_mov_curve", new WiredConfiguration
            {
                IntParams = [7, 100, strength, useVariable ? 1 : 0, 0, 0, 0],
                VariableIds = ["user:99"]
            }).Apply(input, context.Policy.Addons));
        }

        var movement = new WiredRoomMovement(context.Room.GetWired().DispatchWalkTransition);
        f.Owned(() =>
        {
            Assert.True(movement.MoveFurniture(context, f.Items[1], 4, 10, 0, null));
            Assert.True(movement.MoveAvatar(context, rider, 4, 10, true));
        });

        var moves = ReadTrajectoryPublication(packets);
        Assert.Equal(floorExpected, Assert.Single(moves.Where(move => move.Type == 1)).Strength);
        Assert.Equal(avatarExpected, Assert.Single(moves.Where(move => move.Type == 0)).Strength);
        Assert.All(moves, move => Assert.Null(move.Overshoot));
        Assert.Equal(useVariable ? 1 : 0, reads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanonicalStrengthAvatarRetainsTheSameCurveAcrossWalkAndHintCallbacks(bool changeAtHint)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Curve = new(7, 100, 100);
        var packets = CapturePublication(rider);
        var changed = false;
        var capture = rider.GetClient()!.SendCallback;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();

            if (changeAtHint && IsAvatarHint(bytes)) {
                context.Policy.Addons.Curve = new(7, 100, -100);
                changed = true;
            }

            return capture!(args);
        };
        var movement = new WiredRoomMovement((_, _, _) =>
        {
            if (!changeAtHint) {
                context.Policy.Addons.Curve = new(7, 100, -100);
                changed = true;
            }
        });
        f.Owned(() => Assert.True(movement.MoveAvatar(context, rider, 4, 10, true)));

        Assert.True(changed);
        var hint = Assert.Single(packets.Where(IsAvatarHint));
        Assert.Equal(100, BinaryPrimitives.ReadInt32BigEndian(hint.AsSpan(18)));
        Assert.Equal(100, Assert.Single(ReadTrajectoryPublication(packets)).Strength);
    }

    [Fact]
    public async Task OwnerVariableMenuInitializationDoesNotSplitFastQueuePublication()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        room.Type = "private";
        room.OwnerName = rider.GetUsername();
        var packets = CapturePublication(rider);

        await new WiredUserVariablesRequestEvent(new WiredVariableMenuService()).Parse(room, rider.GetClient()!,
            new FlashIncomingPacket { Buffer = Array.Empty<byte>() });

        Assert.Contains(packets, bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredUserVariablesDataComposer);
        packets.Clear();
        f.Advance(250);

        var furniture = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(7, furniture.Length);
        Assert.All(furniture, move =>
        {
            Assert.Equal(move.FromX, move.ToX);
            Assert.Equal(500, move.Duration);
        });
        Assert.Equal((5, 11, 1.0), (rider.X, rider.Y, rider.Z));
    }

    [Theory]
    [InlineData(2, 4, 1)]
    [InlineData(6, 11, -1)]
    public void FastQueueCarryAnimationKeepsSupportedSourcePoseAcrossRealPlacementRefresh(int direction, int startX, int step)
    {
        var f = new Fixture(FastQueueLayout(direction), live: true);
        var rider = f.Walker(1, startX, 10, startX, 11);
        var packets = CapturePublication(rider);

        for (var pulse = 1; pulse <= 3; pulse++) {
            Assert.Equal(1.0, rider.Z);
            packets.Clear();
            f.Advance(pulse == 1 ? 250 : 200);

            var carried = Assert.Single(ReadPublication(packets).Where(move => move.Type == 0));
            Assert.Equal(startX + (pulse - 1) * step, carried.FromX);
            Assert.Equal(startX + pulse * step, carried.ToX);
            Assert.Equal("1", carried.FromZ);
            Assert.Equal("1", carried.ToZ);
            Assert.Equal(200, carried.Duration);
            Assert.Equal(1.0, rider.Z);
        }
    }

    [Fact]
    public void QueuedCarryAnimationKeepsItsPoseThroughOwnerGeometryRefresh()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        context.Policy.Addons.Carry = new(false, new HashSet<int> { rider.VirtualId });
        var passengers = WiredRoomMovement.CapturePassengers(context, [f.Items[1]]);
        var movement = new WiredRoomMovement((_, _, _) => { });
        var packets = CapturePublication(rider);

        Assert.True(movement.MoveFurniture(context, f.Items[1], 5, 11, 0, null, passengers: passengers));
        Assert.Equal((4, 11), (rider.X, rider.Y));
        f.Owned(context.Room.GetGameMap().Navigation!.ApplyDirty);
        Assert.Equal(0.0, rider.Z);
        f.DrainMovement();

        var carried = Assert.Single(ReadPublication(packets).Where(move => move.Type == 0));
        Assert.Equal("1", carried.FromZ);
        // Only the first tile moves in this queued control: its live destination is stacked.
        Assert.Equal("2", carried.ToZ);
        Assert.Equal((5, 11, 2.0), (rider.X, rider.Y, rider.Z));
    }

    [Fact]
    public void OpaqueObserverAndNonemptyProductionVariableDrainSealAndAreConsumedOnce()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        var variables = room.GetWired().Variables;
        var definition = new WiredVariableDefinition(700, room.Id, 1, "pending", WiredVariableTarget.Global,
            WiredVariableAvailability.RoomActive, true);
        var observed = 0;
        var observer = f.Engine.ObserveEvent;
        f.Engine.ObserveEvent = (evt, now) =>
        {
            observer?.Invoke(evt, now);

            if (evt.Kind == WiredEventKind.Variable) {
                observed++;
            }
        };
        var packets = new List<byte[]>();
        var injected = false;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!injected && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                injected = true;
                Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                variables.Module.PersistDefinitionConfiguration(definition.ItemId,
                    before => new(definition, new(before, new WiredVariableValue(1, null, null))));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(injected);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(1, observed);
        Assert.Empty(variables.DrainChanges());
        packets.Clear();
        f.Advance(200);
        Assert.Equal(1, observed);
        Assert.Empty(variables.DrainChanges());
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void NonemptyProductionCounterDrainSealsBeforeItsDisplayPacket()
    {
        var layout = FastQueueLayout(2).Append((20u, 8, 12, 0.0, 0, "wf_upcounter1", (string?)null)).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var wired = f.MovementContext().Room.GetWired();
        wired.AttachRoomItem(f.Items[20]);
        var counters = (WiredCounterController)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        var packets = new List<byte[]>();
        var injected = false;
        var displayed = 0;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);
            var header = BinaryPrimitives.ReadUInt32BigEndian(bytes);

            if (!injected && header == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                injected = true;
                Assert.Empty(ReadPublication(packets).Where(move => move.Type == 1));
                Assert.True(counters.Adjust(f.Items[20], 2, 0, 2));
            }
            else if (header == ServerPacketHeader.ObjectUpdateComposer && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(6)) == 20) {
                displayed++;
                Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(injected);
        Assert.Equal(1, displayed);
        Assert.Equal("1", f.Items[20].LegacyDataString);
        Assert.False(counters.HasPendingChanges);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastQueueImmediateResetPublishesFinalFurnitureEndpointsAndOneCarry(bool carry)
    {
        var layout = FastQueueLayout(2).Where(item => carry || item.Id != 17).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        var movements = ReadPublication(packets);
        var furniture = movements.Where(move => move.Type == 1).ToArray();
        Assert.Equal(7, furniture.Length);
        Assert.All(furniture, move =>
        {
            Assert.Equal(move.FromX, move.ToX);
            Assert.Equal(11, move.ToY);
            Assert.Equal(500, move.Duration);
            Assert.InRange(move.Id, 1, 7);
        });
        Assert.Equal(carry ? 1 : 0, movements.Count(move => move.Type == 0));
        Assert.Equal(carry ? 5 : 4, rider.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FastQueueResetDisabledPublishesSevenForwardSteps(bool carry)
    {
        var layout = FastQueueLayout(2).Where(item => item.Id != 10 && (carry || item.Id != 17)).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        var moves = ReadPublication(packets);
        Assert.Equal(7, moves.Count(move => move.Type == 1));
        Assert.All(moves.Where(move => move.Type == 1), move =>
        {
            Assert.Equal(move.FromX + 1, move.ToX);
            Assert.Equal(200, move.Duration);
        });
        Assert.Equal(carry ? 1 : 0, moves.Count(move => move.Type == 0));
    }

    [Fact]
    public void FastQueuePositiveResetWaitPublishesForwardAndResetSeparately()
    {
        var layout = ChangePublicationConfig(FastQueueLayout(2), 14, config => config with { Delay = 1 });
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);
        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        f.Engine.Remove(11);
        packets.Clear();
        f.Advance(450);
        Assert.Empty(ReadPublication(packets));
        f.Advance(50);

        var reset = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(7, reset.Length);
        Assert.All(reset, move =>
        {
            Assert.Equal(move.FromX - 1, move.ToX);
            Assert.Equal(500, move.Duration);
        });
        Assert.Equal(5, rider.X);
    }

    [Fact]
    public void ArmedPositiveSiblingDoesNotSealTheZeroDeadlineSegment()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 12, 1.0, 0, "wf_act_show_message", JsonSerializer.Serialize(new WiredConfiguration
        { IntParams = [200, 0, 34, 0], Text = "later", Delay = 1 })));
        layout.Add((23, 4, 12, 3.5, 0, "wf_slc_users_bytype", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0] })));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.DoesNotContain(packets, IsChat);
        var timer = (IWiredConfiguredItem)f.Box(11);
        WiredNativeTestSupport.InstallRuntime(timer, timer.Configuration with { IntParams = [10] });
        ((IWiredTimedTrigger)timer).Reset(250);
        packets.Clear();
        f.Advance(500);
        Assert.Contains(packets, IsChat);
        Assert.Empty(ReadPublication(packets));
    }

    [Fact]
    public void StationarySpecialSourceItemSealsBeforeTheWalkCallback()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 11, 0, 0, "bc_tile_1", null));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        f.Items[19].Definition.IsSeat = true;
        var packets = CapturePublication(rider);

        f.Advance(250);

        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(5, rider.X);
        Assert.Equal(4, f.Items[19].GetX);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OpaqueExternalPollAndFlushSealBeforeTheirOutput(bool poll)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        void Output() => rider.GetClient()!.Send(new WiredChatComposer(rider.VirtualId, "barrier", 0, -1, true));

        f.Advance(200);
        packets.Clear();
        Assert.True(f.Engine.Enqueue(new(WiredEventKind.WalkOn) { Actor = rider, EventItem = f.Items[1] }));

        if (poll) {
            f.Engine.PublicationPollIsSilent = () => false;
            f.SetEngineCallback("_pollExternal", (Action<long>)(_ => Output()));
        }
        else {
            f.Engine.PublicationFlushIsSilent = () => false;
            f.SetEngineCallback("_flushExternal", (Action)Output);
        }

        f.Advance(50);

        var moves = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(14, moves.Length);
        Assert.Equal(5, rider.X);
        var firstMove = packets.FindIndex(IsFurnitureMovement);
        Assert.True(packets.Skip(firstMove + 1).Any(IsChat));
    }

    [Fact]
    public void RealWalkRecipientSealsBeforeItsObserverWithoutBypassingFifo()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 7, 12, 0, 0, "wf_trg_walks_on_furni", JsonSerializer.Serialize(new WiredConfiguration
        { IntParams = [100], SelectedItems = [2] })));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var observed = 0;
        f.Engine.ObserveEvent = (evt, _) =>
        {
            if (evt.Kind == WiredEventKind.WalkOn) {
                Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                observed++;
            }
        };
        f.Engine.PublicationObserverIsSilent = _ => false;

        f.Advance(250);

        Assert.True(observed > 0);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(0, 0, 1, 0)]
    public void SnapshotStateRotationAndPartialPositionRemainIndividual(int state, int rotation, int xy, int z)
    {
        var layout = Configure(FastQueueLayout(2), (14, [state, rotation, xy, z, 100]));
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
    }

    [Fact]
    public void SealedSignalTokenCannotJoinALaterSiblingSegment()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 9, 0, 0, "bc_tile_1", null));
        layout.Add((20, 4, 12, 2.1, 0, "wf_act_move_to_dir", JsonSerializer.Serialize(new WiredConfiguration
        { IntParams = [2, 0, 100, 0], SelectedItems = [19] })));
        layout.Add((21, 4, 12, 1.9, 0, "wf_act_show_message", JsonSerializer.Serialize(new WiredConfiguration
        { IntParams = [200, 0, 34, 0], Text = "between" })));
        layout.Add((22, 4, 12, 3, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration())));
        layout.Add((23, 4, 12, 3.5, 0, "wf_slc_users_bytype", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0] })));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        Assert.Equal(new[] { 8, 1, 7 }, packets.Where(IsFurnitureMovement)
            .Select(bytes => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(6))));
        var first = ReadPublication([packets.First(IsFurnitureMovement)]);
        Assert.Equal(7, first.Count(move => move.Type == 1));
        Assert.Equal(0, first[^1].Type);
        var message = packets.FindIndex(IsChat);
        Assert.True(message > packets.FindIndex(IsFurnitureMovement));
        Assert.Equal(5, rider.X);
        Assert.Equal(5, f.Items[19].GetX);
    }

    [Fact]
    public void BudgetYieldSealsBeforeTheQueuedChildResumes()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 12, 3, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration())));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        f.Advance(200);
        f.SetEngineCallback("_limits", new WiredEngineLimits { MaxExecutionsPerPass = 1 });

        f.Advance(50);

        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.All(ReadPublication(packets).Where(move => move.Type == 1), move => Assert.Equal(move.FromX + 1, move.ToX));
        f.SetEngineCallback("_limits", new WiredEngineLimits());
        packets.Clear();
        f.Advance(50);

        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.All(ReadPublication(packets).Where(move => move.Type == 1), move => Assert.Equal(move.FromX - 1, move.ToX));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionCallbackSealsCommittedMovesBeforeNestedOutput()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        f.Walker(2, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var observed = 0;
        f.Engine.ObserveEvent = (evt, _) =>
        {
            if (evt.Kind == WiredEventKind.Collision) {
                Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                rider.GetClient()!.Send(new WiredChatComposer(rider.VirtualId, "collision", 0, -1, true));
                observed++;
            }
        };
        f.Engine.PublicationObserverIsSilent = _ => false;

        f.Advance(250);

        Assert.Equal(1, observed);
        Assert.True(packets.FindIndex(IsChat) > packets.FindIndex(IsFurnitureMovement));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void NonemptyVariableBatchSealsBeforeItsCompletionCallback()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var holder = new WiredVariableHolder(WiredVariableTarget.User, rider.HabboId, rider.VirtualId);
        var builtin = new PublicationBuiltin(() =>
        {
            Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
            rider.GetClient()!.Send(new WiredChatComposer(rider.VirtualId, "batch", 0, -1, true));
        });
        var roomId = f.MovementContext().Room.Id;
        var module = new WiredVariableModule(roomId, new PublicationDirectory(), new MemoryWiredVariableStore(), TimeProvider.System, builtin);
        var frame = new WiredVariableFrame(roomId, [holder]);
        var injected = false;
        f.Engine.PublicationFlushIsSilent = () => true;
        f.SetEngineCallback("_flushExternal", (Action)(() =>
        {
            if (!injected && f.EngineField("_runtimeContext") is WiredRuntimeContext { VariableChanges: { } batch, Publication: { Open: true } }) {
                injected = true;
                batch.Add(module, new(WiredVariableTarget.User, "internal:@direction"), holder, 1, 1, frame);
            }
        }));

        f.Advance(250);

        Assert.True(injected);
        Assert.Equal(1, builtin.Writes);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.True(packets.FindIndex(IsChat) > packets.FindIndex(IsFurnitureMovement));
        Assert.Equal(5, rider.X);
    }

    [Fact]
    public void OpaqueWalkCallbackSealsBeforeNestedOutputAndCall()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var callbacks = 0;
        f.SetBoxField(12, "_movement", new WiredRoomMovement((actor, _, _) =>
        {
            Assert.Single(ReadPublication(packets).Where(move => move.Type == 1));
            actor.GetClient()!.Send(new WiredChatComposer(actor.VirtualId, "nested", 0, -1, true));
            var context = Assert.IsType<WiredRuntimeContext>(f.EngineField("_runtimeContext"));
            Assert.True(f.Engine.CallStacks(context, [f.Items[14]]));
            callbacks++;
        }));

        f.Advance(250);

        Assert.Equal(1, callbacks);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.True(packets.FindIndex(IsChat) > packets.FindIndex(IsFurnitureMovement));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void PublicationDropsAnItemReplacedBeforeFlushAndCannotBeRevived()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var context = f.MovementContext();
        var publication = new WiredFurniturePublication(context.Room, new object(), 0, 0);
        context.Publication = publication;
        var movement = new WiredRoomMovement((_, _, _) => { });
        var original = f.Items[1];
        f.Owned(() => Assert.True(movement.MoveFurniture(context, original, 5, 11, 0, null)));
        Assert.Empty(ReadPublication(packets));
        f.Items[1] = new Item { Id = original.Id, RoomId = original.RoomId, Definition = original.Definition };

        publication.Flush();
        f.Items[1] = original;
        publication.Flush();

        Assert.Empty(ReadPublication(packets));
        Assert.False(publication.Open);
        Assert.False(publication.Append(original, new(1, 1, 4, 11, 0, 5, 11, 0, 0, 0, 200), new(1, 0, 100, 0)));
        Assert.Equal(5, original.GetX);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectCallAndMultipleAntennaSignalsWithOpaqueObserversSealBeforeCarry(bool call)
    {
        var layout = FastQueueLayout(2);

        if (call) {
            layout = ChangePublicationConfig(layout, 13, config => config with { IntParams = [100], SelectedItems = [14] })
                .Select(item => item.Id == 13 ? item with { Name = "wf_act_call_stacks" } : item).ToArray();
        }
        else {
            layout = ChangePublicationConfig(layout, 13, config => config with { SelectedItems = [9, 19] });
            layout = ChangePublicationConfig(layout, 10, config => config with { SelectedItems = [9, 19] });
            layout = [.. layout, (19, 9, 13, 0, 0, "wf_antenna2", null)];
        }

        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var signals = 0;
        f.Engine.ObserveEvent = (evt, _) => { signals += evt.Kind == WiredEventKind.Signal ? 1 : 0; };
        f.Engine.PublicationObserverIsSilent = _ => true;

        f.Advance(250);

        Assert.Equal(call ? 0 : 2, signals);
        var furniture = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(14, furniture.Length);
        Assert.All(furniture.Take(7), move => Assert.Equal(1, move.ToX - move.FromX));
        Assert.All(furniture.Skip(7), move => Assert.Equal(-1, move.ToX - move.FromX));
        Assert.Equal(1, ReadPublication(packets).Count(move => move.Type == 0));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void AddressedAntennaRemovalAfterYieldDoesNotLeakPublicationOrSlots()
    {
        var layout = FastQueueLayout(2).ToList();
        layout.Add((19, 4, 12, 3, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration())));
        var f = new Fixture(layout.ToArray(), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        f.Advance(200);
        f.SetEngineCallback("_limits", new WiredEngineLimits { MaxExecutionsPerPass = 2 });
        f.Advance(50);
        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.True(f.Engine.ReadStats().Pending > 0);
        Assert.True(f.Items.TryRemove(9, out _));
        f.SetEngineCallback("_limits", new WiredEngineLimits());
        packets.Clear();

        f.Advance(50);

        Assert.Empty(ReadPublication(packets));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
        Assert.Equal(5, rider.X);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void HeadingTurnBranchesRemainIndividualPublication(int turn)
    {
        var layout = Configure(FastQueueLayout(2), (12, [2, turn, 100, 1]));
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.Advance(250);

        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(5, rider.X);
    }

    private sealed class PublicationBuiltin(Action completed) : IWiredBuiltinVariables
    {
        private int _value;
        public int Writes;
        public WiredVariableValue? Read(WiredVariableReference reference, WiredVariableHolder holder, WiredVariableFrame frame) => new(_value, null, null);
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame) => throw new NotSupportedException();
        public bool Write(WiredVariableReference reference, WiredVariableHolder holder, int value, WiredVariableFrame frame, out Action? callback)
        {
            Writes++;
            _value = value;
            callback = completed;

            return true;
        }
    }

    private sealed class PublicationDirectory : IWiredVariableDirectory
    {
        public WiredVariableDefinition? Find(uint id) => null;
        public uint? GetRoomOwner(uint roomId) => 5;
    }

    private static (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] ChangePublicationConfig(
        (uint Id, int X, int Y, double Z, int Rot, string Name, string? Json)[] layout, uint id,
        Func<WiredConfiguration, WiredConfiguration> change) => layout.Select(item => item.Id == id
            ? item with { Json = JsonSerializer.Serialize(change(JsonSerializer.Deserialize<WiredConfiguration>(item.Json!)!)) }
            : item).ToArray();

    private static bool IsChat(byte[] bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes) is
        ServerPacketHeader.ChatComposer or ServerPacketHeader.WhisperComposer;
    private static bool IsFurnitureMovement(byte[] bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredMovementsComposer
        && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(10)) == 1;

    private static List<byte[]> CapturePublication(RoomUser user)
    {
        var packets = new List<byte[]>();
        user.GetClient()!.SendCallback = args =>
        {
            packets.Add(args.MemoryBuffer.ToArray());

            return false;
        };

        return packets;
    }

    private sealed record PublishedMove(int Type, int Id, int FromX, int ToX, int ToY, int Duration, string FromZ, string ToZ);

    private static List<PublishedMove> ReadPublication(IEnumerable<byte[]> packets)
    {
        var moves = new List<PublishedMove>();

        foreach (var bytes in packets.Where(bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredMovementsComposer)) {
            var position = 6;
            int Int()
            {
                var value = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position));
                position += 4;

                return value;
            }
            string String()
            {
                var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(position));
                var value = Encoding.UTF8.GetString(bytes, position + 2, length);
                position += 2 + length;

                return value;
            }
            var count = Int();

            for (var index = 0; index < count; index++) {
                var type = Int();
                Assert.InRange(type, 0, 1);
                var fromX = Int();
                _ = Int();
                var toX = Int();
                var toY = Int();
                var fromZ = String();
                var toZ = String();
                var id = Int();

                if (type == 0) {
                    Assert.Equal(1, Int());
                }

                var duration = Int();
                _ = Int();

                if (type == 0) {
                    _ = Int();
                }

                Assert.Equal(0, bytes[position++]);

                if (type == 1) {
                    Assert.Equal(0, bytes[position++]);
                }

                moves.Add(new(type, id, fromX, toX, toY, duration, fromZ, toZ));
            }

            Assert.Equal(bytes.Length, position);
        }

        return moves;
    }
    private sealed record TrajectoryMove(int Type, int Id, int FromX, int ToX, string FromZ, string ToZ,
        int Duration, int? Strength, int? Overshoot);

    private static List<TrajectoryMove> ReadTrajectoryPublication(IEnumerable<byte[]> packets)
    {
        var result = new List<TrajectoryMove>();

        foreach (var bytes in packets.Where(IsMovementBody)) {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            int Int() => BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            string String() => System.Text.Encoding.UTF8.GetString(reader.ReadBytes(BinaryPrimitives.ReadUInt16BigEndian(reader.ReadBytes(2))));
            bool Bool()
            {
                var value = reader.ReadByte();
                Assert.InRange(value, (byte)0, (byte)1);

                return value == 1;
            }
            int? Optional() => Bool() ? Int() : null;
            Assert.Equal(ServerPacketHeader.WiredMovementsComposer, BinaryPrimitives.ReadUInt32BigEndian(bytes));
            reader.BaseStream.Position = 6;
            var count = Int();

            for (var index = 0; index < count; index++) {
                var type = Int();
                Assert.InRange(type, 0, 1);
                var fromX = Int();
                _ = Int();
                var toX = Int();
                _ = Int();
                var fromZ = String();
                var toZ = String();
                var id = Int();

                if (type == 0) {
                    Assert.Equal(1, Int());
                }

                var duration = Int();
                _ = Int();

                if (type == 0) {
                    _ = Int();
                }

                var first = Optional();
                var strength = type == 0 ? first : Optional();
                result.Add(new(type, id, fromX, toX, fromZ, toZ, duration, strength, type == 1 ? first : null));
            }

            Assert.Equal(bytes.Length, reader.BaseStream.Position);
        }

        return result;
    }

}
