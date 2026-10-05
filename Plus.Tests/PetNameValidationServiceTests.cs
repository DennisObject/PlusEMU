using System.Reflection;
using Plus.Communication.Packets;
using Plus.Communication.Packets.Incoming.Catalog;
using Plus.Communication.Packets.Outgoing;
using Plus.Communication.Flash;
using Plus.HabboHotel.Catalog.Pets;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Rooms.Chat.Filter;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public class PetNameValidationServiceTests
{
    [Fact]
    public async Task HandlerConsumesTheNameAndOnlyDelegates()
    {
        var service = new RecordingNames();
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        var packet = HabbiconTestSupport.Incoming("Fluffy");
        await new CheckPetNameEvent(service).Parse(client, packet);
        Assert.Equal("Fluffy", service.Name);
        Assert.False(packet.HasDataRemaining());
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("x", PetNameError.TooShort, "2", 0)]
    [InlineData("1234567890123456", PetNameError.TooLong, "15", 0)]
    [InlineData("bad!", PetNameError.InvalidCharacters, "", 0)]
    [InlineData("blocked", PetNameError.Filtered, "", 1)]
    [InlineData("Fluffy", PetNameError.None, "", 1)]
    public void ValidationKeepsLengthCharacterFilterOrderAndExactErrorPayload(string name,
        PetNameError error, string extra, int expectedFilterReads)
    {
        var reads = 0;
        var filter = DispatchProxy.Create<IWordFilterManager, WordFilter>();
        ((WordFilter)(object)filter).Check = value => { reads++; Assert.Equal(name, value); return value == "blocked"; };
        var (client, sent) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        new PetNameValidationService(filter).Check(client, name);
        var response = Assert.Single(sent);
        Assert.Equal(ServerPacketHeader.CheckPetNameComposer, response.Header);
        var body = new FlashIncomingPacket { Buffer = response.Payload };
        Assert.Equal(((int)error, extra), (body.ReadInt(), body.ReadString()));
        Assert.False(body.HasDataRemaining());
        Assert.Equal(expectedFilterReads, reads);
    }

    private sealed class RecordingNames : IPetNameValidationService
    {
        public string? Name;
        public void Check(GameClient session, string name) => Name = name;
    }

    public class WordFilter : DispatchProxy
    {
        public Func<string, bool> Check { get; set; } = _ => false;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name == "IsFiltered"
            ? Check((string)args![0]!) : throw new NotSupportedException(method?.Name);
    }
}
