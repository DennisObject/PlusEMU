using System.Buffers.Binary;
using System.Text.Json;
using System.Reflection;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.Wired.Variables;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.HabboHotel.Rooms.PathFinding;
using Xunit;

namespace Plus.Tests;

public sealed partial class WiredGlideConveyorTests
{
    [Fact]
    public async Task OwnerVariablePollingPublishesEachCarryWithItsActualFurnitureEntries()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        room.Type = "private";
        room.OwnerName = rider.GetUsername();
        var packets = CapturePublication(rider);
        var request = new WiredUserVariablesRequestEvent(new WiredVariableMenuService());
        var observed = new List<List<PublishedMove[]>>();
        var rawBodies = new List<byte[]>();
        var rotation = (rider.RotBody, rider.RotHead);

        for (var pulse = 1; pulse <= 3; pulse++) {
            await request.Parse(room, rider.GetClient()!, new FlashIncomingPacket { Buffer = new byte[] { 0, 0, 0, 1 } });
            Assert.Contains(packets, bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredUserVariablesData64Composer);
            packets.Clear();

            f.Advance(pulse == 1 ? 250 : 200);

            var groups = packets.Where(bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredMovementsComposer)
                .Select(bytes => ReadPublication([bytes]).ToArray()).ToList();
            observed.Add(groups);
            rawBodies.AddRange(packets.Where(IsMovementBody));
            var moves = groups.SelectMany(group => group).ToArray();
            var furniture = moves.Where(move => move.Type == 1).ToArray();
            Assert.Equal(pulse == 1 ? 7 : 6, furniture.Length);
            Assert.Equal(Enumerable.Range(1, 7).Where(id => pulse == 1 || id != pulse - 1), furniture.Select(move => move.Id));
            Assert.All(furniture, move =>
            {
                Assert.Equal(move.FromX, move.ToX);
                Assert.Equal(11, move.ToY);
                Assert.Equal(500, move.Duration);
            });
            var carry = Assert.Single(moves.Where(move => move.Type == 0));

            Assert.Equal(3 + pulse, carry.FromX);
            Assert.Equal(4 + pulse, carry.ToX);
            Assert.Equal("1", carry.FromZ);
            Assert.Equal("1", carry.ToZ);
            Assert.Equal(200, carry.Duration);
            Assert.Equal((4 + pulse, 11, 1.0), (rider.X, rider.Y, rider.Z));
            Assert.Equal(0, f.Engine.ReadStats().Pending);
        }

        Assert.True(observed.All(groups => groups.Count == 1),
            string.Join("; ", observed.Select((groups, index) => $"pulse{index + 1}: "
                + string.Join(" | ", groups.Select(group => string.Join(",", group.Select(move => $"{move.Type}:{move.Id}")))))));
        Assert.All(observed, groups => Assert.Equal(0, groups[0][^1].Type));

        foreach (var (body, pulse) in rawBodies.Select((body, index) => (body, index + 1))) {
            AssertOctaneMixedFields(body, 3 + pulse, rider.VirtualId, rotation.RotBody, rotation.RotHead);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyOwnerVariablePollingKeepsImmediateCarryAndBaselineFurniture(bool full)
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        Assert.False(room.UsesV2Movement);
        Assert.Null(room.GetGameMap().Navigation);
        Assert.False(RoomOwnerScope.IsOwner(room));
        room.Type = "private";
        room.OwnerName = rider.GetUsername();
        var packets = CapturePublication(rider);
        var request = new WiredUserVariablesRequestEvent(new WiredVariableMenuService());
        var observations = new List<List<PublishedMove[]>>();

        for (var pulse = 1; pulse <= 3; pulse++) {
            await request.Parse(room, rider.GetClient()!, new FlashIncomingPacket { Buffer = new byte[] { 0, 0, 0, 1 } });
            Assert.Contains(packets, bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredUserVariablesData64Composer);
            packets.Clear();
            f.AdvanceWiredPass(pulse == 1 ? 250 : 200, full);
            var groups = packets.Where(IsMovementBody).Select(bytes => ReadPublication([bytes]).ToArray()).ToList();
            observations.Add(groups);
            var furniture = groups.SelectMany(group => group).Where(move => move.Type == 1).ToArray();
            Assert.Equal(pulse == 1 ? 7 : 6, furniture.Length);
            Assert.Equal(Enumerable.Range(1, 7).Where(id => pulse == 1 || id != pulse - 1), furniture.Select(move => move.Id));
            Assert.All(furniture, move =>
            {
                Assert.Equal(move.FromX, move.ToX);
                Assert.Equal(11, move.ToY);
                Assert.Equal("0", move.FromZ);
                Assert.Equal("0", move.ToZ);
                Assert.Equal(500, move.Duration);
            });
            var carry = Assert.Single(groups.SelectMany(group => group).Where(move => move.Type == 0));
            Assert.Equal((3 + pulse, 4 + pulse, 11, 200, "1", "1"),
                (carry.FromX, carry.ToX, carry.ToY, carry.Duration, carry.FromZ, carry.ToZ));
            Assert.Equal((4 + pulse, 11, 1.0), (rider.X, rider.Y, rider.Z));
            Assert.Equal(0, f.Engine.ReadStats().Pending);
        }

        Assert.All(observations, groups =>
        {
            Assert.Equal(2, groups.Count);
            Assert.Equal(0, Assert.Single(groups[0]).Type); // Prior Legacy avatar body remains immediate.
            Assert.All(groups[1], move => Assert.Equal(1, move.Type));
        });
    }

    [Fact]
    public void LegacyFallbackRejectsShadowDespiteItsImmediateAvatarPath()
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "shadow");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        Assert.False(room.UsesV2Movement);
        Assert.NotNull(room.GetGameMap().Navigation);
        var packets = CapturePublication(rider);

        f.AdvanceWiredPass(250, false);

        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.All(packets.Where(IsMovementBody), bytes =>
            Assert.True(ReadPublication([bytes]).All(move => move.Type == 1) || ReadPublication([bytes]).Count == 1));
        Assert.Equal((5, 11, 1.0), (rider.X, rider.Y, rider.Z));
    }

    [Fact]
    public void LegacyFallbackRejectsReplacedClassifierDuringCarryEligibility()
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);
        var carryReads = 0;
        var ordinaryReads = 0;
        f.Engine.PublicationObserverIsSilent = _ =>
        {
            if (f.EngineField("_executingAction") is { } action
                && ((IWiredItem)action.GetType().GetProperty("Box")!.GetValue(action)!).Item.Id == 12) {
                carryReads++;
            }
            else {
                ordinaryReads++;
                Assert.NotEmpty(ReadPublication(packets));
            }

            return true;
        };

        f.AdvanceWiredPass(250, false);

        Assert.Equal(0, carryReads);
        Assert.True(ordinaryReads > 0); // Existing normal queued dispatch still invokes its classifier.
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData("wf_trg_walks_on_furni")]
    [InlineData("wf_trg_collision")]
    public void LegacyFallbackRejectsKnownWalkOrCollisionRegistryWork(string trigger)
    {
        var layout = FastQueueLayout(2).Append((20u, 7, 12, 0.0, 0, trigger,
            JsonSerializer.Serialize(new WiredConfiguration { IntParams = trigger == "wf_trg_collision" ? [] : [100], SelectedItems = [1, 2, 3, 4, 5, 6, 7] }))).ToArray();
        var f = new Fixture(layout, live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        f.AdvanceWiredPass(250, false);

        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(5, rider.X);
        f.AdvanceWiredPass(100, false);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyImmediateHintRetainsExactlyOneBodyAcrossLoggedFailure(bool throws)
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var hints = 0;
        var movementsAtHint = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;
                movementsAtHint = ReadPublication(packets).Count;

                if (throws) {
                    throw new InvalidOperationException("logged legacy hint failure");
                }
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.Equal(1, hints);
        Assert.Equal(0, movementsAtHint);
        Assert.Equal(7, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(0, ReadPublication([packets.First(IsMovementBody)])[0].Type);
    }

    [Fact]
    public void LegacyImmediateHintReentrantMutationSealsBeforeObserverOutput()
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var hints = 0;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;
                Assert.Empty(ReadPublication(packets));
                Assert.True(f.Engine.Mutate(() =>
                {
                    Assert.Single(ReadPublication(packets), move => move.Type == 1);
                    rider.GetClient()!.Send(new WiredChatComposer(rider.VirtualId, "legacy observer", 0, -1, true));

                    return true;
                }));
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.Equal(1, hints);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.True(packets.FindIndex(IsChat) > packets.FindIndex(IsMovementBody));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyPreparedActorCannotSurviveASecondFailedPreparation(bool staleActor)
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var reset = false;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!reset && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.HeightMapUpdateComposer
                && f.EngineField("_runtimeContext") is WiredRuntimeContext context) {
                reset = true;
                var movement = (WiredRoomMovement)typeof(WiredModernAction).GetField("_movement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(f.Box(12))!;
                var prepare = (Action<WiredRuntimeContext, IReadOnlyList<RoomUser>>)typeof(WiredRoomMovement)
                    .GetField("_prepareCarryPublication", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(movement)!;
                var unknown = new RoomUser(99, context.Room.Id, 99, context.Room, rider.GetClient(), TestChatEmotions.Unused, TestRewardProgress.Unused);
                prepare(context, staleActor ? [unknown] : []);
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.True(reset);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(5, rider.X);
    }

    [Fact]
    public void LegacyDifferentPassengersKeepTheirImmediateBodiesSeparate()
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var first = f.Walker(1, 4, 10, 4, 11);
        var second = f.Walker(2, 7, 10, 7, 11);
        var packets = CapturePublication(first);

        f.AdvanceWiredPass(250, false);

        var groups = packets.Where(IsMovementBody).Select(bytes => ReadPublication([bytes]).ToArray()).ToArray();
        Assert.Equal(3, groups.Length);
        Assert.Equal((0, 1), (Assert.Single(groups[0]).Type, groups[0][0].Id));
        Assert.Equal((0, 2), (Assert.Single(groups[1]).Type, groups[1][0].Id));
        Assert.Equal(new[] { 1, 2, 4, 5, 6, 7 }, groups[2].Select(move => move.Id));
        Assert.All(groups[2], move => Assert.Equal((1, move.FromX, 500), (move.Type, move.ToX, move.Duration)));
        Assert.Equal((5, 8), (first.X, second.X));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyPreparedAppendRejectsChangedActorOrCapturedConfiguration(bool actorReuse)
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var changed = false;
        var handled = true;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!changed && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.HeightMapUpdateComposer
                && f.EngineField("_runtimeContext") is WiredRuntimeContext context) {
                changed = true;
                var action = (WiredModernAction)f.Box(12);

                if (actorReuse) {
                    var replacement = new RoomUser(99, context.Room.Id, rider.VirtualId, context.Room, rider.GetClient(),
                        TestChatEmotions.Unused, TestRewardProgress.Unused)
                    { X = rider.X, Y = rider.Y, Z = rider.Z };
                    Assert.NotEqual(rider.Movement.LifetimeId, replacement.Movement.LifetimeId);
                    f.ReplaceUser(replacement);
                }
                else {
                    action.ApplyConfiguration(action.Configuration with { Text = "new configuration, same motion" });
                }

                var movement = (WiredRoomMovement)typeof(WiredModernAction).GetField("_movement", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(action)!;
                var append = (Func<WiredRuntimeContext, RoomUser, WiredMovementComposer, WiredMoveStyleComposer, bool>)typeof(WiredRoomMovement)
                    .GetField("_appendCarryPublication", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(movement)!;
                // Probe the actual prepared adapter while its original action still runs, before returning to placement.
                handled = append(context, rider, new(0, rider.VirtualId, 4, 11, 1, 5, 11, 1, 0, 0, 200), new(rider.VirtualId, 0, 100, 0, true));
                Assert.False(context.Publication!.Open);
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.True(changed);
        Assert.False(handled);
        var floors = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(Enumerable.Range(1, 7), floors.Where(move => move.ToX > move.FromX).Select(move => move.Id));
        Assert.Equal(Enumerable.Range(actorReuse ? 2 : 1, actorReuse ? 6 : 7),
            floors.Where(move => move.ToX < move.FromX).Select(move => move.Id));
        // The replacement stays at X4: existing occupancy prevents tile1 resetting to that tile.
        Assert.Equal(actorReuse ? 5 : 4, f.Items[1].GetX);
        Assert.Equal(actorReuse ? 13 : 14, floors.Length);
        Assert.Equal(actorReuse ? 0 : 1, ReadPublication(packets).Count(move => move.Type == 0));
        Assert.Equal(actorReuse ? 4 : 5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void LegacyNonemptyProductionVariableDrainSealsTheRetainedFloorSegment()
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        var variables = room.GetWired().Variables;
        var definition = new WiredVariableDefinition(700, room.Id, 1, "legacy pending", WiredVariableTarget.Global,
            WiredVariableAvailability.RoomActive, true);
        var packets = new List<byte[]>();
        var injected = false;
        var persisted = false;
        var movementsAtHint = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!injected && IsAvatarHint(bytes)) {
                injected = true;
                movementsAtHint = ReadPublication(packets).Count;
                variables.Module.PersistDefinitionConfiguration(definition.ItemId,
                    before => new(definition, new(before, new WiredVariableValue(1, null, null))));
                persisted = true;
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.True(injected);
        Assert.True(persisted);
        Assert.Equal(0, movementsAtHint);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Empty(variables.DrainChanges());
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void LegacyNonemptyProductionCounterDrainSealsBeforeItsDisplay()
    {
        var layout = FastQueueLayout(2).Append((20u, 8, 12, 0.0, 0, "wf_upcounter1", (string?)null)).ToArray();
        var f = new Fixture(layout, live: true, movementEngine: "legacy");
        var rider = f.Walker(1, 4, 10, 4, 11);
        var wired = f.MovementContext().Room.GetWired();
        wired.AttachRoomItem(f.Items[20]);
        var counters = (WiredCounterController)typeof(Plus.HabboHotel.Rooms.Instance.WiredComponent)
            .GetField("_counters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(wired)!;
        var packets = new List<byte[]>();
        var injected = false;
        var displayed = 0;
        var movementsAtHint = -1;
        var movementsAtDisplay = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);
            var header = BinaryPrimitives.ReadUInt32BigEndian(bytes);

            if (!injected && IsAvatarHint(bytes)) {
                injected = true;
                movementsAtHint = ReadPublication(packets).Count;
                Assert.True(counters.Adjust(f.Items[20], 2, 0, 2));
            }
            else if (header == ServerPacketHeader.ObjectUpdateComposer && BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(6)) == 20) {
                displayed++;
                movementsAtDisplay = ReadPublication(packets).Count(move => move.Type == 1);
            }

            return false;
        };

        f.AdvanceWiredPass(250, false);

        Assert.True(injected);
        Assert.Equal(1, displayed);
        Assert.Equal(0, movementsAtHint);
        Assert.Equal(7, movementsAtDisplay);
        Assert.False(counters.HasPendingChanges);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CarryHintFailureDoesNotRetryOrDuplicateItsRetainedMovement(bool throws)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var hints = 0;
        var movementsAtHint = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;
                movementsAtHint = ReadPublication(packets).Count;

                if (throws) {
                    throw new InvalidOperationException("actual client hint callback");
                }
            }

            return false;
        };

        f.Advance(250);

        Assert.Equal(1, hints);
        Assert.Equal(0, movementsAtHint);
        Assert.Equal(5, rider.X);
        var body = Assert.Single(packets.Where(IsMovementBody));
        Assert.Equal(8, ReadPublication([body]).Count);
        Assert.Single(ReadPublication([body]), move => move.Type == 0);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CarryHintReentrantSealOwnsTheOnlyMovementPublication()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var hints = 0;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;
                Assert.Empty(ReadPublication(packets));
                Assert.True(f.Engine.Mutate(() => true));
                Assert.Single(ReadPublication(packets), move => move.Type == 0);
            }

            return false;
        };

        f.Advance(250);

        Assert.Equal(1, hints);
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void DifferentPassengersKeepDistinctEntriesInTheSameMovementBody()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var first = f.Walker(1, 4, 10, 4, 11);
        var second = f.Walker(2, 7, 10, 7, 11);
        var packets = CapturePublication(first);

        f.Advance(250);

        var moves = ReadPublication([Assert.Single(packets.Where(IsMovementBody))]);
        Assert.Equal(new[] { 1, 2 }, moves.Where(move => move.Type == 0).Select(move => move.Id));
        Assert.Equal(new[] { 1, 2, 4, 5, 6, 7 }, moves.Where(move => move.Type == 1).Select(move => move.Id));
        Assert.All(moves.Take(6), move => Assert.Equal(1, move.Type));
        Assert.All(moves.Skip(6), move => Assert.Equal(0, move.Type));
        Assert.Equal((5, 8), (first.X, second.X));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActorDepartureOrReuseAtFlushDropsOnlyItsOldMovement(bool reuse)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var first = f.Walker(1, 4, 10, 4, 11);
        var second = f.Walker(2, 7, 10, 7, 11);
        var room = f.MovementContext().Room;
        var packets = new List<byte[]>();
        var changed = false;
        var hints = new List<int>();
        first.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints.Add(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(10)));
            }

            if (!changed && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && !IsAvatarHint(bytes)) {
                changed = true;

                if (reuse) {
                    var replacement = new RoomUser(99, room.Id, first.VirtualId, room, first.GetClient(), TestChatEmotions.Unused, TestRewardProgress.Unused)
                    { X = first.X, Y = first.Y, Z = first.Z };
                    f.ReplaceUser(replacement);
                    Assert.NotEqual(first.Movement.LifetimeId, replacement.Movement.LifetimeId);
                }
                else {
                    first.Movement.State = NavState.Removing;
                }
            }

            return false;
        };

        f.Advance(250);

        Assert.True(changed);
        Assert.Equal(new[] { 1, 2 }, hints); // Already emitted hints are not retrospectively removed.
        var moves = ReadPublication(packets);
        Assert.NotEmpty(moves.Where(move => move.Type == 1));
        Assert.Equal(second.VirtualId, Assert.Single(moves, move => move.Type == 0).Id);
        Assert.Equal((5, 8), (first.X, second.X));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(false, "v2")]
    [InlineData(true, "v2")]
    [InlineData(false, "legacy")]
    [InlineData(true, "legacy")]
    public void RepeatedActorPublishesEarlierBodyBeforeItsLaterCommitAndHintEvenForEqualStyles(bool differentStyle, string movementEngine)
    {
        var layout = FastQueueLayout(2).Where(item => item.Id != 10 && item.Id != 14 && item.Id != 15).ToList();
        var heading = layout.Single(item => item.Id == 12);
        layout[layout.FindIndex(item => item.Id == 13)] = (13, 4, 12, 1.3, 0, "wf_act_call_stacks",
            JsonSerializer.Serialize(new WiredConfiguration { IntParams = [100], SelectedItems = [20] }));
        layout.Add((19, 4, 12, 3.5, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration())));
        layout.Add((20, 6, 12, 0, 0, heading.Name, heading.Json));
        layout.Add((21, 6, 12, 0.65, 0, "wf_xtra_mov_carry_users", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [0, 900] })));
        layout.Add((22, 6, 12, 1.3, 0, "wf_xtra_mov_physics", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0, 0, 900, 900, 900] })));
        layout.Add((23, 6, 12, 1.95, 0, "wf_xtra_anim_time", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [200] })));

        if (differentStyle) {
            layout.Add((24, 6, 12, 2.6, 0, "wf_xtra_mov_curve", JsonSerializer.Serialize(new WiredConfiguration { IntParams = [2, 50, 80, 0, 0, 0, 0] })));
        }

        var f = new Fixture(layout.ToArray(), live: true, movementEngine: movementEngine);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = new List<byte[]>();
        var hints = 0;
        PublishedMove[] earlier = [];
        var laterStyle = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;

                if (hints == 2) {
                    earlier = ReadPublication(packets).Where(move => move.Type == 0).ToArray();
                    laterStyle = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(14));
                }
            }

            return false;
        };

        f.Advance(250);

        Assert.Equal(2, hints);
        Assert.Equal((4, 5), (Assert.Single(earlier).FromX, earlier[0].ToX));
        Assert.Equal(differentStyle ? 2 : 0, laterStyle);
        Assert.Equal(new[] { (4, 5), (5, 6) }, ReadPublication(packets).Where(move => move.Type == 0).Select(move => (move.FromX, move.ToX)));
        Assert.Equal(6, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void OffOwnerFactoryCarryKeepsItsQueuedImmediatePublicationFallback()
    {
        var layout = FastQueueLayout(2).Select(item => item.Id == 11
            ? item with { Name = "wf_trg_says_something", Json = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0], Text = "pulse" }) }
            : item).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var packets = CapturePublication(rider);

        rider.OnChat(0, "pulse", false);
        Assert.Empty(ReadPublication(packets).Where(move => move.Type == 0));
        Assert.Equal(4, rider.X);
        f.DrainMovement();

        var avatar = Assert.Single(packets.Where(bytes => IsMovementBody(bytes) && ReadPublication([bytes]).Any(move => move.Type == 0)));
        Assert.Single(ReadPublication([avatar]));
        Assert.Equal(5, rider.X);
    }

    [Theory]
    [InlineData(false, "v2")]
    [InlineData(true, "v2")]
    [InlineData(false, "legacy")]
    [InlineData(true, "legacy")]
    public void OrdinaryAvatarMovementDoesNotJoinAnOpenFurnitureToken(bool factory, string movementEngine)
    {
        var f = new Fixture(FastQueueLayout(2), live: true, movementEngine: movementEngine);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var context = f.MovementContext();
        var publication = new WiredFurniturePublication(context.Room, new object(), 0, 0);
        context.Publication = publication;
        var packets = new List<byte[]>();
        (int X, int Y)? poseAtSeal = null;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer) {
                poseAtSeal ??= (rider.X, rider.Y);
            }

            return false;
        };
        var movement = factory
            ? (WiredRoomMovement)typeof(WiredModernAction).GetField("_movement",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(f.Box(12))!
            : new WiredRoomMovement((_, _, _) => { });
        Assert.True(publication.Append(f.Items[1], new(1, 1, 4, 11, 0, 4, 11, 0, 0, 0, 500), new(1, 0, 100, 0)));

        f.Owned(() => Assert.True(movement.MoveAvatar(context, rider, 4, 10, true)));

        Assert.False(publication.Open);
        Assert.Equal((4, 11), poseAtSeal!.Value);
        Assert.Equal(new[] { 1, 0 }, ReadPublication(packets).Select(move => move.Type));
        Assert.All(packets.Where(IsMovementBody), bytes => Assert.Single(ReadPublication([bytes])));
        Assert.Equal((4, 10), (rider.X, rider.Y));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionCallOrMultiAntennaSignalKeepsTheSameMixedSegment(bool call)
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

        f.Advance(250);

        var moves = ReadPublication([Assert.Single(packets.Where(IsMovementBody))]);
        Assert.Equal(Enumerable.Range(1, 7), moves.Where(move => move.Type == 1).Select(move => move.Id));
        Assert.All(moves.Take(7), move =>
        {
            Assert.Equal(move.FromX, move.ToX);
            Assert.Equal(500, move.Duration);
        });
        var carry = Assert.Single(moves, move => move.Type == 0);
        Assert.Equal((4, 5, 200, "1", "1"), (carry.FromX, carry.ToX, carry.Duration, carry.FromZ, carry.ToZ));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void TrustedProductionVariableDrainSealsAfterTheOriginalAvatarHint()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var variables = f.MovementContext().Room.GetWired().Variables;
        var definition = new WiredVariableDefinition(700, f.MovementContext().Room.Id, 1, "pending", WiredVariableTarget.Global,
            WiredVariableAvailability.RoomActive, true);
        var packets = new List<byte[]>();
        var injected = false;
        var beforeHint = -1;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!injected && IsAvatarHint(bytes)) {
                injected = true;
                beforeHint = ReadPublication(packets).Count;
                variables.Module.PersistDefinitionConfiguration(definition.ItemId,
                    before => new(definition, new(before, new WiredVariableValue(1, null, null))));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(injected);
        Assert.Equal(0, beforeHint);
        var first = ReadPublication([packets.First(IsMovementBody)]);
        Assert.Equal(8, first.Count);
        Assert.Equal(0, first[^1].Type);
        Assert.Equal(14, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Single(ReadPublication(packets), move => move.Type == 0);
        Assert.Empty(variables.DrainChanges());
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActorDepartureOrReuseInsideHintCannotFallbackOrPublishTheOldBody(bool reuse)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 4, 10, 4, 11);
        var room = f.MovementContext().Room;
        var packets = new List<byte[]>();
        var hints = 0;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (IsAvatarHint(bytes)) {
                hints++;

                if (reuse) {
                    var replacement = new RoomUser(99, room.Id, rider.VirtualId, room, rider.GetClient(), TestChatEmotions.Unused, TestRewardProgress.Unused)
                    { X = rider.X, Y = rider.Y, Z = rider.Z };
                    f.ReplaceUser(replacement);
                }
                else {
                    rider.Movement.State = NavState.Removing;
                }

                f.Engine.Mutate(() => true);
            }

            return false;
        };

        f.Advance(250);

        Assert.Equal(1, hints);
        // The hint is inside the first mover: its callback seals before the remaining six commits.
        var first = Assert.Single(ReadPublication([packets.First(IsMovementBody)]));
        Assert.Equal((1, 1, 4, 5, 200), (first.Type, first.Id, first.FromX, first.ToX, first.Duration));
        var floors = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        Assert.Equal(Enumerable.Range(1, 7), floors.Where(move => move.ToX > move.FromX).Select(move => move.Id));
        Assert.DoesNotContain(ReadPublication(packets), move => move.Type == 0);
        Assert.Equal(5, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    private static void AssertOctaneMixedFields(byte[] bytes, int fromX, int actorId, int bodyRotation, int headRotation)
    {
        var packet = new FlashIncomingPacket { Buffer = bytes.AsMemory(6) };
        var count = packet.ReadInt();

        for (var index = 0; index < count; index++) {
            var avatar = index == count - 1;
            Assert.Equal(avatar ? 0 : 1, packet.ReadInt());
            var sourceX = packet.ReadInt();
            Assert.Equal(11, packet.ReadInt());
            Assert.Equal(avatar ? fromX + 1 : sourceX, packet.ReadInt());
            Assert.Equal(11, packet.ReadInt());
            Assert.Equal(avatar ? "1" : "0", packet.ReadString());
            Assert.Equal(avatar ? "1" : "0", packet.ReadString());
            var id = packet.ReadInt();

            if (avatar) {
                Assert.Equal(actorId, id);
                Assert.Equal(fromX, sourceX);
                Assert.Equal(1, packet.ReadInt()); // Existing active Octane slide mode.
                Assert.Equal(bodyRotation, packet.ReadInt());
                Assert.Equal(headRotation, packet.ReadInt());
                Assert.Equal(200, packet.ReadInt());
            }
            else {
                Assert.Equal(id + 3, sourceX);
                Assert.Equal(0, packet.ReadInt());
                Assert.Equal(500, packet.ReadInt());
                Assert.Equal(0, packet.ReadInt());
                Assert.Equal(0, packet.ReadInt());
                Assert.Equal(0, packet.ReadInt());
            }
        }

        Assert.False(packet.HasDataRemaining());
    }

    private static bool IsMovementBody(byte[] bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredMovementsComposer;
    private static bool IsAvatarHint(byte[] bytes) => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1;
}
