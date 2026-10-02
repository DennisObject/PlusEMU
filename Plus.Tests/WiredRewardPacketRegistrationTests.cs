using System.Text.Json;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Revisions;
using Plus.HabboHotel.Items.Wired.Modern.Actions;
using Xunit;

namespace Plus.Tests;

public class WiredRewardPacketRegistrationTests
{
    [Theory]
    [InlineData("1.6.6.json")]
    [InlineData("example.json")]
    public void ConcreteRewardResultUsesActiveCollisionFreeProfileMapping(string profile)
    {
        var revision = JsonSerializer.Deserialize<Revision>(File.ReadAllText(Path.Join(AppContext.BaseDirectory, "revisions", profile)))!;
        var composer = new WiredRewardResultComposer(5);
        Assert.Equal(ServerPacketHeader.WiredRewardResultComposer, composer.MessageId);
        Assert.Equal(178u, revision.OutgoingHeaders[nameof(ServerPacketHeader.WiredRewardResultComposer)]);
        Assert.Single(revision.OutgoingHeaders, pair => pair.Value == 178);
    }
}
