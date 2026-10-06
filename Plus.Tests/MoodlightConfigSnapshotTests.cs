using System.Runtime.CompilerServices;
using Plus.Communication.Packets.Outgoing.Rooms.Furni.Moodlight;
using Plus.HabboHotel.Items.Data.Moodlight;
using Xunit;

namespace Plus.Tests;

public sealed class MoodlightConfigSnapshotTests
{
    [Fact]
    public void ConfigWritesTheCurrentPresetAndEveryPresetInLegacyOrder()
    {
        var data = Data();
        var packet = new HabbiconTestSupport.RecordingPacket();

        new MoodlightConfigComposer(MoodlightConfigSnapshot.Capture(data)).Compose(packet);

        Assert.Equal(new object[]
        {
            3, 2,
            1, 1, "#000000", 255,
            2, 2, "#0053F7", 100,
            3, 1, "#EA4532", 0,
        }, packet.Writes);
    }

    [Fact]
    public void PreparedConfigSurvivesPresetAndListMutationOnEveryRecomposition()
    {
        var data = Data();
        var composer = new MoodlightConfigComposer(MoodlightConfigSnapshot.Capture(data));
        var original = new HabbiconTestSupport.RecordingPacket();
        composer.Compose(original);

        data.CurrentPreset = 3;
        data.Presets[0].BackgroundOnly = true;
        data.Presets[0].ColorCode = "#74F5F5";
        data.Presets[0].ColorIntensity = 1;
        data.Presets.Clear();
        data.Presets.Add(new("#F2F851", 200, true));

        for (var i = 0; i < 2; i++) {
            var recomposed = new HabbiconTestSupport.RecordingPacket();
            composer.Compose(recomposed);
            Assert.Equal(original.Writes, recomposed.Writes);
        }
    }

    [Fact]
    public void EmptyConfigRetainsTheCurrentPresetAndZeroCount()
    {
        var data = Data();
        data.Presets.Clear();
        var packet = new HabbiconTestSupport.RecordingPacket();

        new MoodlightConfigComposer(MoodlightConfigSnapshot.Capture(data)).Compose(packet);

        Assert.Equal(new object[] { 0, 2 }, packet.Writes);
    }

    private static MoodlightData Data()
    {
        var data = (MoodlightData)RuntimeHelpers.GetUninitializedObject(typeof(MoodlightData));
        data.CurrentPreset = 2;
        data.Presets = [new("#000000", 255, false), new("#0053F7", 100, true), new("#EA4532", 0, false)];

        return data;
    }
}
