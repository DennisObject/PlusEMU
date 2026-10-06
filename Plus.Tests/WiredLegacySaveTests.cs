using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Plus.Communication.Flash;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Conditions;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Boxes.Triggers;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public class WiredLegacySaveTests
{
    [Fact]
    public void TypedRepeaterPreparationMatchesPacketDelayAndTickCount()
    {
        var item = new Item { Id = 7, Definition = new() };
        var original = new RepeaterBox(null!, item) { Delay = 3, TickCount = 500 };
        Assert.True(WiredLegacySave.TryPrepare(original, Incoming(1, 9, "", 0, 0), WiredBoxCategory.Trigger,
            source => new RepeaterBox(null!, source.Item), out var packetCandidate, out var packetError));
        Assert.True(WiredLegacyProtocol.TryRead(Incoming(1, 9, "", 0, 0), WiredBoxCategory.Trigger, out var proposed));
        Assert.True(WiredLegacySave.TryPrepare(original, proposed, WiredBoxCategory.Trigger,
            source => new RepeaterBox(null!, source.Item), out var typedCandidate, out var typedError));

        Assert.Empty(packetError);
        Assert.Empty(typedError);
        var packetCycle = Assert.IsAssignableFrom<IWiredCycle>(packetCandidate);
        var typedCycle = Assert.IsAssignableFrom<IWiredCycle>(typedCandidate);
        // Nine half-seconds: the room fires it on the ninth tick, after waiting eight.
        Assert.Equal((9, 8), (packetCycle.Delay, packetCycle.TickCount));
        Assert.Equal((packetCycle.Delay, packetCycle.TickCount), (typedCycle.Delay, typedCycle.TickCount));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(120, true)]
    [InlineData(121, false)]
    public void LegacyRepeaterSavesOnlyItsEditorRange(int delay, bool accepted)
    {
        var original = new RepeaterBox(null!, new Item { Id = 7, Definition = new() }) { Delay = 3 };
        Assert.Equal(accepted, WiredLegacySave.TryPrepare(original, Incoming(1, delay, "", 0, 0), WiredBoxCategory.Trigger,
            source => new RepeaterBox(null!, source.Item), out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RealLegacyHandlersPrepareDetachedCandidatesAndKeepTheirExistingStorageFields(int shape)
    {
        var original = Original(shape);
        var selected = original.SetItems;
        Assert.True(WiredLegacySave.TryPrepare(original, Payload(shape), Envelope(shape),
            source => Create(shape, source.Item), out var candidate, out var error));
        Assert.Empty(error);
        Assert.NotNull(candidate);
        Assert.NotSame(original, candidate);
        Assert.Same(original.Item, candidate.Item);
        Assert.Equal("previous", original.StringData);
        Assert.Equal("8:1,2,3,4,state;", original.ItemsData);
        Assert.Same(selected, original.SetItems);
        Assert.Single(original.SetItems);
        Assert.NotSame(selected, candidate.SetItems);
        Assert.True(candidate.BoolData);
        // SaveBox captures replacement snapshots on the detached candidate during persistence.
        Assert.Equal(original.ItemsData, candidate.ItemsData);
        Assert.Equal(shape switch { 1 or 4 => "1;0;1", 2 => "NEW_BADGE", 3 => "5", _ => "previous" }, candidate.StringData);
        if (original is IWiredCycle oldCycle && candidate is IWiredCycle nextCycle)
        {
            Assert.Equal(3, oldCycle.Delay);
            Assert.Equal(shape == 0 ? 9 : 8, nextCycle.Delay);
        }
        Assert.Equal(shape == 2 ? 1 : 0, candidate.SetItems.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TypedLegacyPreparationPreservesTheExistingFieldMappings(int shape)
    {
        var original = Original(shape);
        Assert.True(WiredLegacyProtocol.TryRead(Payload(shape), Envelope(shape), out var proposed));

        Assert.True(WiredLegacySave.TryPrepare(original, proposed, Envelope(shape),
            source => Create(shape, source.Item), out var candidate, out var error));

        Assert.Empty(error);
        Assert.Equal(shape switch { 1 or 4 => "1;0;1", 2 => "NEW_BADGE", 3 => "5", _ => "previous" }, candidate!.StringData);
        if (candidate is IWiredCycle cycle) Assert.Equal(shape == 0 ? 9 : 8, cycle.Delay);
        Assert.Equal(shape == 2 ? 1 : 0, candidate.SetItems.Count);
        Assert.Equal("previous", original.StringData);
        Assert.Single(original.SetItems);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void TruncatedLegacyEnvelopesNeverReachTheFactoryOrPersistence(int shape)
    {
        var original = Original(shape);
        var packet = Payload(shape);
        packet.Buffer = packet.Buffer[..^1];
        Assert.False(WiredLegacySave.TrySave(original, packet, Envelope(shape),
            _ => throw new InvalidOperationException("Factory must not run."),
            (_, _) => throw new InvalidOperationException("Persistence must not run."), out _));
        Assert.Equal("previous", original.StringData);
        Assert.Single(original.SetItems);
        if (original is IWiredCycle cycle) Assert.Equal(3, cycle.Delay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FailedLegacyPersistenceDoesNotMutateOriginalOrItsSelections(int shape)
    {
        var original = Original(shape);
        var selected = original.SetItems;
        Assert.Throws<IOException>(() => WiredLegacySave.TrySave(original, Payload(shape), Envelope(shape),
            source => Create(shape, source.Item), (live, candidate) =>
            {
                Assert.Same(original, live);
                Assert.NotSame(live, candidate);
                candidate.ItemsData = "candidate snapshot prepared by persistence";
                throw new IOException("Database unavailable.");
            }, out _));
        Assert.Equal("previous", original.StringData);
        Assert.Equal("8:1,2,3,4,state;", original.ItemsData);
        Assert.Same(selected, original.SetItems);
        Assert.Single(original.SetItems);
        if (original is IWiredCycle cycle) Assert.Equal(3, cycle.Delay);
    }

    [Fact]
    public void MalformedLegacyCountsValuesSelectionsAndTrailingFieldsAreRejectedBeforeReplay()
    {
        var teleport = Original(0);
        Assert.False(WiredLegacySave.TryPrepare(teleport, Incoming(1, 55, "", 0, 9, 0),
            WiredBoxCategory.Action, _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacySave.TryPrepare(teleport, Incoming(0, "", 0, 9, 0, 123),
            WiredBoxCategory.Action, _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacySave.TryPrepare(teleport, Incoming(0, "", 1, 999, 9, 0),
            WiredBoxCategory.Action, _ => throw new Exception("Factory must not run."), out _, out _, id => id == 8));
        Assert.False(WiredLegacySave.TryPrepare(Original(1), Incoming(3, 1, 2, 0, "", 0, 8, 0),
            WiredBoxCategory.Action, _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacySave.TryPrepare(Original(3), Incoming(0, "invalid speed", 0, 0, 0),
            WiredBoxCategory.Action, _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.Equal("previous", teleport.StringData);
        Assert.Single(teleport.SetItems);
    }

    [Fact]
    public void FactoryCannotReturnTheLiveBoxAndDetachedPublicationCannotReportSuccess()
    {
        var original = Original(2);
        Assert.False(WiredLegacySave.TryPrepare(original, Payload(2), WiredBoxCategory.Action,
            source => source, out _, out _));
        Assert.Equal("previous", original.StringData);
        Assert.False(WiredLegacySave.TrySave(original, Payload(2), WiredBoxCategory.Action,
            source => Create(2, source.Item), (_, _) => false, out var error));
        Assert.NotEmpty(error);
        Assert.Equal("previous", original.StringData);
    }

    [Fact]
    public void PublisherReceivesOnlyPreparedCandidateWithoutHoldingTheBoxLock()
    {
        var original = Original(2);
        var published = false;
        Assert.True(WiredLegacySave.TrySave(original, Payload(2), WiredBoxCategory.Action,
            source => Create(2, source.Item), (live, candidate) =>
            {
                Assert.False(Monitor.IsEntered(live));
                Assert.Equal("NEW_BADGE", candidate.StringData);
                Assert.Equal("previous", live.StringData);
                published = true;
                return true;
            }, out var error));
        Assert.True(published);
        Assert.Empty(error);
    }

    private static WiredBoxCategory Envelope(int shape) => shape == 4 ? WiredBoxCategory.Condition : WiredBoxCategory.Action;

    private static IWiredItem Original(int shape)
    {
        var box = Create(shape, new Item { Id = 7, Definition = new() });
        box.StringData = "previous";
        box.BoolData = true;
        box.ItemsData = "8:1,2,3,4,state;";
        box.SetItems = new ConcurrentDictionary<uint, Item>();
        box.SetItems.TryAdd(8, new Item { Id = 8 });
        if (box is IWiredCycle cycle) cycle.Delay = 3;
        return box;
    }

    private static IWiredItem Create(int shape, Item item) => shape switch
    {
        0 => new TeleportUserBox(null!, item),
        1 => new MatchPositionBox(null!, item),
        2 => new GiveUserBadgeBox(null!, item, TestWiredAccess.Unused),
        3 => new SetRollerSpeedBox(null!, item),
        4 => new FurniMatchStateAndPositionBox(null!, item),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static FlashIncomingPacket Payload(int shape) => shape switch
    {
        0 => Incoming(0, "", 0, 9, 0),
        1 => Incoming(3, 1, 0, 1, "", 0, 8, 0),
        2 => Incoming(0, "NEW_BADGE", 0, 0, 0),
        3 => Incoming(0, "5", 0, 0, 0),
        4 => Incoming(3, 1, 0, 1, "", 0, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(shape))
    };

    private static FlashIncomingPacket Incoming(params object[] values)
    {
        using var stream = new MemoryStream();
        Span<byte> number = stackalloc byte[4];
        foreach (var value in values)
        {
            if (value is int integer)
            {
                BinaryPrimitives.WriteInt32BigEndian(number, integer);
                stream.Write(number);
            }
            else
            {
                var text = Encoding.UTF8.GetBytes((string)value);
                BinaryPrimitives.WriteUInt16BigEndian(number, (ushort)text.Length);
                stream.Write(number[..2]);
                stream.Write(text);
            }
        }
        return new() { Buffer = stream.ToArray() };
    }
}
