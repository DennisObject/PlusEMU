using Plus.Communication.Packets.Outgoing.Rooms.Furni.Wired;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.Wired;
using Plus.HabboHotel.Items.Wired.Boxes.Effects;
using Plus.HabboHotel.Items.Wired.Configuration;
using Xunit;

namespace Plus.Tests;

public class WiredLegacyCustomEditorTests
{
    [Theory]
    [InlineData(0, 119)]
    [InlineData(1, 88)]
    [InlineData(2, 123)]
    public void RealCustomBoxesOpenThroughActiveEditorsWithoutChangingStoredFields(int kind, int code)
    {
        var original = Create(kind);
        var text = original.StringData;
        Assert.True(WiredLegacyCustomEditor.TryGetConfiguration(original, out var descriptor, out var configuration));
        Assert.Equal(code, descriptor.EditorCode);
        Assert.Equal(kind == 1 ? new[] { 2 } : new[] { 0, 0, 34 }, configuration.IntParams);
        var packet = new RecordingPacket();
        new WiredEffectConfigComposer(original, []).Compose(packet);
        var expected = new List<object> { false, 0, 0, 91, 7u, kind == 1 ? "" : text, kind == 1 ? 1 : 3 };
        expected.AddRange(configuration.IntParams.Select(value => (object)value));
        expected.AddRange(new object[] { 0, code, 0, 0 });
        Assert.Equal(expected, packet.Writes);
        Assert.Equal(text, original.StringData);
        Assert.Equal("8;", original.ItemsData);
        Assert.True(original.BoolData);
    }

    [Theory]
    [InlineData(0, "NEW_BADGE")]
    [InlineData(1, "5")]
    [InlineData(2, "")]
    public void CurrentEditorFieldsTranslateToLegacyCandidatesBeforeRealEnginePublication(int kind, string expected)
    {
        var original = Create(kind);
        var previous = original.StringData;
        var proposal = Proposal(kind);
        Assert.True(WiredLegacyCustomEditor.TryPrepare(original, proposal, WiredLegacyCustomEditor.CreateCandidate,
            out var candidate, out _));
        Assert.Equal(expected, candidate!.StringData);
        Assert.Equal(previous, original.StringData);
        Assert.Equal("8;", candidate.ItemsData);
        var engine = Engine(original);
        Assert.Throws<IOException>(() => engine.PublishLegacy(original, candidate, () => throw new IOException("DB unavailable.")));
        Assert.Equal(previous, original.StringData);
        var persisted = false;
        Assert.True(engine.PublishLegacy(original, candidate, () =>
        {
            Assert.Equal(previous, original.StringData);
            Assert.Equal(expected, candidate.StringData);
            persisted = true;
        }));
        Assert.True(persisted);
        Assert.Equal(expected, original.StringData);
        Assert.Equal("8;", original.ItemsData);
    }

    [Fact]
    public void UnsupportedCustomSourceSpeedAndChangedRegenerateTextAreRejectedWithoutMutation()
    {
        var badge = Create(0);
        Assert.False(WiredLegacyCustomEditor.TryPrepare(badge, Proposal(0) with { IntParams = [200, 0, 34] },
            _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacyCustomEditor.TryPrepare(badge, Proposal(0) with { Text = new string('B', 51) },
            _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacyCustomEditor.TryPrepare(Create(1), Proposal(1) with { IntParams = [11] },
            _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacyCustomEditor.TryPrepare(Create(2), Proposal(2) with { Text = "ignored setting" },
            _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.False(WiredLegacyCustomEditor.TryPrepare(badge, Proposal(0) with { SelectedItems = [8] },
            _ => throw new Exception("Factory must not run."), out _, out _));
        Assert.Equal("OLD_BADGE", badge.StringData);
    }

    private static WiredStackEngine Engine(IWiredItem original)
    {
        var engine = new WiredStackEngine(() => 0, box => ReferenceEquals(box.Item, original.Item), _ => true, _ => { }, _ => { });
        Assert.True(engine.Add(original));
        return engine;
    }

    private static IWiredItem Create(int kind)
    {
        var item = new Item { Id = 7, Definition = new() { SpriteId = 91, InteractionType = InteractionType.WiredEffect } };
        IWiredItem box = kind switch
        {
            0 => new GiveUserBadgeBox(null!, item),
            1 => new SetRollerSpeedBox(null!, item),
            2 => new RegenerateMapsBox(null!, item),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        box.StringData = kind switch { 0 => "OLD_BADGE", 1 => "2", _ => "" };
        box.ItemsData = "8;";
        box.BoolData = true;
        return box;
    }

    private static WiredConfiguration Proposal(int kind) => kind switch
    {
        0 => new() { IntParams = [0, 0, 34], Text = "NEW_BADGE" },
        1 => new() { IntParams = [5] },
        2 => new() { IntParams = [0, 0, 34] },
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed class RecordingPacket : IOutgoingPacket
    {
        public List<object> Writes { get; } = [];
        public int MessageId { get; set; }
        public ReadOnlyMemory<byte> Buffer => ReadOnlyMemory<byte>.Empty;
        public void WriteByte(byte value) => Writes.Add(value);
        public void WriteShort(short value) => Writes.Add(value);
        public void WriteInt(int value) => Writes.Add(value);
        public void WriteInteger(int value) => Writes.Add(value);
        public void WriteUInt(uint value) => Writes.Add(value);
        public void WriteUInteger(uint value) => Writes.Add(value);
        public void WriteBool(bool value) => Writes.Add(value);
        public void WriteBoolean(bool value) => Writes.Add(value);
        public void WriteString(string value) => Writes.Add(value);
        public void WriteDouble(double value) => Writes.Add(value);
    }
}
