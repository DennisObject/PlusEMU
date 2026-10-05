using Plus.Communication.Packets.Outgoing.Rooms.Furni;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class OpenGiftComposerTests
{
    [Fact]
    public void PreparedWireDataRecomposesIdentically()
    {
        var (client, sent) = HabbiconTestSupport.Client(new Habbo());
        var data = new OpenGiftWireData("Floor", 42, "chair", 7, true, "blue");
        var composer = new OpenGiftComposer(data);

        client.Send(composer);
        client.Send(composer);

        Assert.Equal(2, sent.Count);
        Assert.Equal(sent[0].Payload, sent[1].Payload);
    }
}
