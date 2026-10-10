using System.Buffers.Binary;
using System.Text.Json;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Configuration;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Plus.HabboHotel.Items.Wired.Modern.Triggers;
using Plus.HabboHotel.Items.Wired.Runtime;
using Plus.HabboHotel.Rooms;
using Plus.Communication.Flash;
using Plus.Communication.Packets.Incoming.WiredVariables;
using Plus.Communication.Packets.Outgoing;
using Plus.HabboHotel.Items.Wired.Variables;
using Xunit;

namespace Plus.Tests;

public sealed partial class WiredGlideConveyorTests
{
    [Theory]
    [InlineData(2, 4, 1, false)]
    [InlineData(2, 4, 1, true)]
    [InlineData(6, 11, -1, false)]
    [InlineData(6, 11, -1, true)]
    public async Task OwnerVariablePollingKeepsOccupiedRiderHeadingAndResetInOnePublication(int direction, int startX, int step, bool reverseCreation)
    {
        var f = new Fixture(FastQueueLayout(direction, reverseCreation), live: true);
        var rider = f.Walker(1, startX, 10, startX, 11);
        var room = f.MovementContext().Room;
        room.Type = "private";
        room.OwnerName = rider.GetUsername();
        var packets = CapturePublication(rider);
        var request = new WiredUserVariablesRequestEvent(new WiredVariableMenuService());

        for (var pulse = 1; pulse <= 3; pulse++) {
            await request.Parse(room, rider.GetClient()!, new FlashIncomingPacket { Buffer = Array.Empty<byte>() });
            Assert.Contains(packets, bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredUserVariablesDataComposer);
            packets.Clear();
            f.Advance(pulse == 1 ? 250 : 200);

            var furniture = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
            var movingIds = ((IWiredConfiguredItem)f.Box(12)).Configuration.SelectedItems;
            var collides = pulse > 1 && (direction == 2 != reverseCreation);
            var blockedX = startX + (pulse - 2) * step;
            var omitted = !collides ? 0 : reverseCreation ? 12 - blockedX : blockedX - 3;
            Assert.Equal(movingIds.Where(id => id != omitted).Order(), furniture.Select(move => (uint)move.Id).Order());
            Assert.Equal(collides ? 6 : 7, furniture.Length);
            Assert.DoesNotContain(furniture, move => move.Id == omitted);
            Assert.All(furniture, move =>
            {
                Assert.Equal(move.FromX, move.ToX);
                Assert.Equal(11, move.ToY);
                Assert.Equal(500, move.Duration);
            });
            var carry = Assert.Single(ReadPublication(packets).Where(move => move.Type == 0));
            Assert.Equal(startX + (pulse - 1) * step, carry.FromX);
            Assert.Equal(startX + pulse * step, carry.ToX);
            Assert.Equal("1", carry.FromZ);
            Assert.Equal("1", carry.ToZ);
            Assert.Equal(startX + pulse * step, rider.X);
            Assert.Equal(0, f.Engine.ReadStats().Pending);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionOpaqueObserverOrClassifierSealsBeforeCallbackOutput(bool classifier)
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var callbacks = 0;
        void Output()
        {
            Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
            rider.GetClient()!.Send(new WiredChatComposer(rider.VirtualId, "opaque", 0, -1, true));
            callbacks++;
        }

        if (classifier) {
            f.Engine.PublicationObserverIsSilent = _ => { Output(); return true; };
        }
        else {
            f.Engine.ObserveEvent = (evt, _) =>
            {
                if (evt.Kind == WiredEventKind.Collision) {
                    Output();
                }
            };
        }

        f.Advance(250);

        Assert.Equal(classifier ? 0 : 1, callbacks);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(8, rider.X);
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionCustomPublisherWithProductionContextIsNotBypassed()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var wired = f.MovementContext().Room.GetWired();
        var observed = 0;
        f.SetBoxField(12, "_publish", (Action<WiredRuntimeEvent>)(evt =>
        {
            Assert.Equal(WiredEventKind.Collision, evt.Kind);
            Assert.Equal(2, ReadPublication(packets).Count(move => move.Type == 1));
            observed++;
            wired.Dispatch(evt);
        }));

        f.Advance(250);

        Assert.Equal(1, observed);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionRejectsQueueOrDepthOnlyOnceAfterSealing(bool depth)
    {
        var f = new Fixture(FastQueueLayout(2).Where(item => item.Id != 13 && item.Id != 17).ToArray(), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var refusals = 0;
        f.Engine.LimitReached = (limit, _) =>
        {
            Assert.Equal(depth ? WiredEngineLimit.Depth : WiredEngineLimit.PendingStacks, limit);
            Assert.Equal(2, ReadPublication(packets).Count(move => move.Type == 1));
            refusals++;
        };
        f.Advance(200);
        f.SetEngineCallback("_limits", depth ? new WiredEngineLimits { MaxDepth = 0 } : new WiredEngineLimits { MaxPendingStacks = 1 });

        f.Advance(50);

        Assert.Equal(1, refusals);
        Assert.Equal(6, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }


    [Fact]
    public void CollisionOpaqueEventsGetterIsInvokedOnlyAfterSealing()
    {
        var layout = FastQueueLayout(2).Append((19u, 8, 12, 0.0, 0, "wf_trg_collision", JsonSerializer.Serialize(new WiredConfiguration()))).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var readsDuringExecution = 0;
        var old = (IWiredConfiguredItem)f.Box(19);
        var probe = new OpaqueCollisionEvents(f.MovementContext().Room, f.Items[19], old.Descriptor, () =>
        {
            if (f.EngineField("_executingAction") is { } action
                && ((IWiredItem)action.GetType().GetProperty("Box")!.GetValue(action)!).Item.Id == 12) {
                Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                readsDuringExecution++;
            }
        });
        Assert.True(f.Engine.Remove(19));
        Assert.True(f.Engine.Add(probe));

        f.Advance(250);

        Assert.True(readsDuringExecution > 0);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionQueuedProofRechecksObserverWithoutChangingFifoOrRepeatingAdmission()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = new List<byte[]>();
        var changed = false;
        var order = new List<WiredEventKind>();
        var collisionFirst = false;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!changed && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                changed = true;
                var queue = (System.Collections.IEnumerable)f.EngineField("_dispatches")!;
                var collisions = queue.Cast<object>().Where(pending => ((WiredRuntimeEvent)pending.GetType().GetProperty("Event")!.GetValue(pending)!).Kind == WiredEventKind.Collision).ToArray();
                var collision = Assert.Single(collisions);
                collisionFirst = !queue.Cast<object>().TakeWhile(pending => !ReferenceEquals(pending, collision))
                    .Any(pending => ((WiredRuntimeEvent)pending.GetType().GetProperty("Event")!.GetValue(pending)!).Kind == WiredEventKind.Signal);
                Assert.Empty((IWiredContextualTrigger[])collision.GetType().GetField("Triggers")!.GetValue(collision)!);
                Assert.NotNull(collision.GetType().GetField("CollisionProof")!.GetValue(collision));
                Assert.Empty(ReadPublication(packets).Where(move => move.Type == 1));
                f.Engine.ObserveEvent = (evt, _) =>
                {
                    order.Add(evt.Kind);

                    if (evt.Kind == WiredEventKind.Collision) {
                        Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                    }
                };
            }

            return false;
        };

        f.Advance(250);

        Assert.True(changed);
        Assert.Equal(1, order.Count(kind => kind == WiredEventKind.Collision));
        Assert.Equal(collisionFirst, order.IndexOf(WiredEventKind.Collision) < order.IndexOf(WiredEventKind.Signal));
        Assert.Equal(collisionFirst ? 12 : 6, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
        Assert.Equal(8, rider.X);
    }

    private sealed class OpaqueCollisionEvents(Room room, Item item, WiredBoxDescriptor descriptor, Action read)
        : WiredModernTrigger(room, item, descriptor), IWiredContextualTrigger
    {
        IReadOnlyCollection<WiredEventKind> IWiredContextualTrigger.Events
        {
            get
            {
                read();

                return [];
            }
        }
    }

    [Fact]
    public void CollisionFactoryDoesNotTrustAClassifierReplacedBeforeActionCreation()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var called = 0;
        f.Engine.PublicationObserverIsSilent = _ => { called++; return true; };
        var errors = new List<Exception>();
        f.SetEngineCallback("_error", (Action<Exception>)errors.Add);
        var configuration = ((IWiredConfiguredItem)f.Box(12)).Configuration;
        var replacement = f.MovementContext().Room.GetWired().CreateConfiguredBox(f.Items[12], ((IWiredConfiguredItem)f.Box(12)).Descriptor)!;
        replacement.ApplyConfiguration(configuration);
        Assert.True(replacement.TryValidateConfiguration(configuration, out _, out _));
        Assert.Equal("wf_act_move_to_dir", replacement.Descriptor.CanonicalName);
        Assert.True(f.Engine.Remove(12));
        Assert.True(f.Engine.Add(replacement));

        // Removing the old box resets this tile timer; first poll arms its period.
        f.Advance(300);

        Assert.Empty(errors);
        Assert.Equal(0, called);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionOpaqueFastWorkBindingSealsBeforeReadingIt()
    {
        var layout = FastQueueLayout(2).Append((19u, 4, 12, 3.5, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration()))).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var countsAtReads = new List<int>();
        f.SetEngineCallback("_externalFastWork", (Func<bool>)(() =>
        {
            if (f.EngineField("_executingAction") is { } action
                && ((IWiredItem)action.GetType().GetProperty("Box")!.GetValue(action)!).Item.Id == 12) {
                countsAtReads.Add(ReadPublication(packets).Count(move => move.Type == 1));
            }

            return false;
        }));

        f.Advance(250);

        Assert.NotEmpty(countsAtReads);
        Assert.All(countsAtReads, count => Assert.True(count > 0));
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionNonemptyProductionVariableDrainSealsAnAlreadyCapturedProof()
    {
        var f = new Fixture(FastQueueLayout(2), live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var room = f.MovementContext().Room;
        var variables = room.GetWired().Variables;
        var definition = new WiredVariableDefinition(700, room.Id, 1, "pending", WiredVariableTarget.Global,
            WiredVariableAvailability.RoomActive, true);
        var packets = new List<byte[]>();
        var injected = false;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!injected && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                injected = true;
                Assert.NotNull(CapturedCollision(f).GetType().GetField("CollisionProof")!.GetValue(CapturedCollision(f)));
                Assert.Empty(ReadPublication(packets).Where(move => move.Type == 1));
                variables.Module.PersistDefinitionConfiguration(definition.ItemId,
                    before => new(definition, new(before, new WiredVariableValue(1, null, null))));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(injected);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Empty(variables.DrainChanges());
        Assert.Equal(0, f.Engine.ReadStats().Pending);
        Assert.Equal(8, rider.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionCapturedSourcePlacementOrActorReuseCannotPreserveTheSegment(bool actorReuse)
    {
        var layout = FastQueueLayout(2).Append((19u, 4, 12, 3.5, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration()))).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = new List<byte[]>();
        var changed = false;
        RoomUser? replacement = null;
        object? capturedEnvelope = null;
        var signalFirst = false;
        WiredFurniturePublication? oldPublication = null;
        var sawReset = false;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!changed && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                changed = true;
                var captured = CapturedCollision(f);
                capturedEnvelope = captured;
                signalFirst = ((System.Collections.IEnumerable)f.EngineField("_dispatches")!).Cast<object>()
                    .TakeWhile(pending => !ReferenceEquals(pending, captured))
                    .Any(pending => ((WiredRuntimeEvent)pending.GetType().GetProperty("Event")!.GetValue(pending)!).Kind == WiredEventKind.Signal);
                var proof = captured.GetType().GetField("CollisionProof")!.GetValue(captured);
                Assert.NotNull(proof);
                oldPublication = (WiredFurniturePublication)proof.GetType().GetProperty("Publication")!.GetValue(proof)!;

                if (actorReuse) {
                    replacement = new RoomUser(99, f.MovementContext().Room.Id, rider.VirtualId, f.MovementContext().Room,
                        rider.GetClient(), TestChatEmotions.Unused, TestRewardProgress.Unused)
                    { X = rider.X, Y = rider.Y, Z = rider.Z };
                    f.ReplaceUser(replacement);
                }
                else {
                    var source = ((WiredRuntimeEvent)captured.GetType().GetProperty("Event")!.GetValue(captured)!).EventItem!;
                    source.Detach(f.MovementContext().Room);
                    source.Attach(f.MovementContext().Room, TestItemRuntime.Interactors, TestItemRuntime.Travel, TestItemRuntime.Rewards);
                }
            }

            if (changed && !sawReset && ReadPublication([bytes]).Any(move => move.Type == 1 && move.Duration == 500)) {
                sawReset = true;
                Assert.False(oldPublication!.Open);
                Assert.Equal(6, ReadPublication(packets).Count(move => move.Type == 1 && move.Duration == 200));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(changed);
        Assert.False(signalFirst);
        Assert.True(sawReset);
        var furniture = ReadPublication(packets).Where(move => move.Type == 1).ToArray();
        var forwardIds = new[] { 1, 2, 4, 5, 6, 7 };
        // The replacement at X8 blocks item5's reset from X9; the original actor is no longer live.
        var resetIds = actorReuse ? new[] { 1, 2, 4, 6, 7 } : forwardIds;
        Assert.Equal(forwardIds, furniture.Where(move => move.Duration == 200).Select(move => move.Id));
        Assert.Equal(resetIds, furniture.Where(move => move.Duration == 500).Select(move => move.Id));
        Assert.All(furniture, move => Assert.Equal(move.FromX + (move.Duration == 200 ? 1 : -1), move.ToX));
        Assert.Same(rider, ((WiredRuntimeEvent)capturedEnvelope!.GetType().GetProperty("Event")!.GetValue(capturedEnvelope)!).Actor);
        Assert.Equal(!actorReuse, (bool)capturedEnvelope!.GetType().GetField("Initialized")!.GetValue(capturedEnvelope)!);
        Assert.Equal(0, f.Engine.ReadStats().Pending);

        if (replacement != null) {
            Assert.Equal(8, replacement.X);
            Assert.Equal(9, f.Items[5].GetX);
            Assert.Same(replacement, f.MovementContext().Room.GetRoomUserManager().GetRoomUserByVirtualId(rider.VirtualId));
            Assert.NotEqual(rider.Movement.LifetimeId, replacement.Movement.LifetimeId);
        }
    }

    [Fact]
    public void CollisionDirtyRegistryAtDrainKeepsItsOriginalEmptyHandlerSnapshot()
    {
        var layout = FastQueueLayout(2).Append((19u, 8, 12, 0.0, 0, "wf_trg_collision", JsonSerializer.Serialize(new WiredConfiguration()))).Append((20u, 4, 12, 3.5, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration()))).ToArray();
        var f = new Fixture(layout, live: true);
        var added = f.Box(19);
        Assert.True(f.Engine.Remove(19));
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = new List<byte[]>();
        var changed = false;
        object? captured = null;
        rider.GetClient()!.SendCallback = args =>
        {
            var bytes = args.MemoryBuffer.ToArray();
            packets.Add(bytes);

            if (!changed && BinaryPrimitives.ReadUInt32BigEndian(bytes) == ServerPacketHeader.WiredFurniMoveStyleComposer && bytes[^1] == 1) {
                changed = true;
                captured = CapturedCollision(f);
                Assert.NotNull(captured.GetType().GetField("CollisionProof")!.GetValue(captured));
                Assert.True(f.Engine.Add(added));
            }

            return false;
        };

        f.Advance(250);

        Assert.True(changed);
        Assert.Empty((IWiredContextualTrigger[])captured!.GetType().GetField("Triggers")!.GetValue(captured)!);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    [Fact]
    public void CollisionWithoutAKnownTimerSealsBeforeTheFastWorkObserverCanChange()
    {
        var layout = FastQueueLayout(2).Select(item => item.Id == 11
            ? item with
            {
                Name = "wf_trg_says_something",
                Json = JsonSerializer.Serialize(new WiredConfiguration { IntParams = [1, 0, 0], Text = "pulse" })
            } : item).Append((19u, 4, 12, 3.5, 0, "wf_xtra_exec_in_order", JsonSerializer.Serialize(new WiredConfiguration()))).ToArray();
        var f = new Fixture(layout, live: true);
        var rider = f.Walker(1, 7, 10, 7, 11);
        var packets = CapturePublication(rider);
        var stopped = 0;
        f.SetEngineCallback("_fastWorkObserver", (Action<bool>)(active =>
        {
            if (!active) {
                Assert.NotEmpty(ReadPublication(packets).Where(move => move.Type == 1));
                stopped++;
            }
        }));

        f.Owned(() => rider.OnChat(0, "pulse", false));
        f.Advance(100);

        Assert.Equal(1, stopped);
        Assert.Equal(12, ReadPublication(packets).Count(move => move.Type == 1));
        Assert.Equal(0, f.Engine.ReadStats().Pending);
    }

    private static object CapturedCollision(Fixture f) => Assert.Single(((System.Collections.IEnumerable)f.EngineField("_dispatches")!).Cast<object>(),
        pending => ((WiredRuntimeEvent)pending.GetType().GetProperty("Event")!.GetValue(pending)!).Kind == WiredEventKind.Collision);
}
