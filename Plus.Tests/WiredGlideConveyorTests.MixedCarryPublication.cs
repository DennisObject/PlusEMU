using System.Buffers.Binary;
using System.Text.Json;
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
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedActorSealsBeforeItsLaterCommitAndHintEvenForEqualStyles(bool differentStyle)
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

        var f = new Fixture(layout.ToArray(), live: true);
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
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryAvatarMovementDoesNotJoinAnOpenFurnitureToken(bool factory)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
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
