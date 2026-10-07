using Plus.Communication.Packets.Incoming.Crafting;
using Plus.Communication.Packets.Outgoing.Crafting;
using Plus.HabboHotel.Crafting;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Users;
using Xunit;

namespace Plus.Tests;

public sealed class CraftingPacketTests
{
    [Fact]
    public void NativeAirAndV75ProductsContainRecipeProductAndFurnitureClass()
    {
        var packet = new HabbiconTestSupport.RecordingPacket();
        var product = new CraftingProduct("recipe", "product", "furniture");
        new CraftableProductsComposer([product], ["ingredient"]).Compose(packet);
        Assert.Equal(new object[] { 1, "recipe", "product", "furniture", 1, "ingredient" }, packet.Writes);
        packet = new HabbiconTestSupport.RecordingPacket();
        new CraftingResultComposer(product).Compose(packet);
        Assert.Equal(new object[] { true, "recipe", "product", "furniture" }, packet.Writes);
        packet = new HabbiconTestSupport.RecordingPacket();
        new CraftingResultComposer(null).Compose(packet);
        Assert.Equal(new object[] { false }, packet.Writes);
        packet = new HabbiconTestSupport.RecordingPacket();
        new CraftingRecipeComposer([(2, "ingredient")]).Compose(packet);
        Assert.Equal(new object[] { 1, 2, "ingredient" }, packet.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProductLayoutsLeaveTheFollowingIngredientCountAtTheCorrectOffset(bool native)
    {
        using var stream = Plus.Communication.Flash.PlusMemoryStream.GetStream();
        var outgoing = new Plus.Communication.Flash.FlashOutgoingPacket(stream);
        var product = new CraftingProduct("recipe", "product", "furniture");
        new CraftableProductsComposer([product], ["ingredient"], native).Compose(outgoing);
        var incoming = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = stream.ToArray()[6..] };
        Assert.Equal(1, incoming.ReadInt());
        Assert.Equal("recipe", incoming.ReadString());

        if (native) {
            Assert.Equal("product", incoming.ReadString());
        }

        Assert.Equal("furniture", incoming.ReadString());
        Assert.Equal(1, incoming.ReadInt());
        Assert.Equal("ingredient", incoming.ReadString());
        Assert.False(incoming.HasDataRemaining());
        var result = new HabbiconTestSupport.RecordingPacket();
        new CraftingResultComposer(product, native).Compose(result);
        Assert.Equal(native ? new object[] { true, "recipe", "product", "furniture" }
            : [true, "recipe", "furniture"], result.Writes);
    }

    [Fact]
    public async Task EveryCraftingHandlerDecodesAndDelegatesItsNativeContract()
    {
        var service = new Recorder();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        await new GetCraftableProductsEvent(service).Parse(client, HabbiconTestSupport.Incoming(10));
        await new GetCraftingRecipeEvent(service).Parse(client, HabbiconTestSupport.Incoming("recipe"));
        await new CraftEvent(service).Parse(client, HabbiconTestSupport.Incoming(10, "recipe"));
        await new GetCraftingRecipesAvailableEvent(service).Parse(client, HabbiconTestSupport.Incoming(10, 2, 20, 21));
        await new CraftSecretEvent(service).Parse(client, HabbiconTestSupport.Incoming(10, 2, 20, 21));
        Assert.Equal(new[] { "products:10", "recipe:recipe", "craft:10:recipe", "available:10:20,21", "secret:10:20,21" }, service.Calls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(51)]
    [InlineData(int.MaxValue)]
    [InlineData(2)]
    public async Task InvalidOrTruncatedListsCannotAllocateOrReadUnboundedIngredients(int count)
    {
        var service = new Recorder();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        await new CraftSecretEvent(service).Parse(client, HabbiconTestSupport.Incoming(10, count));
        Assert.Equal("secret:10:0", Assert.Single(service.Calls));
    }

    [Fact]
    public async Task EverySupportedBrowserRevisionMapsAllFiveRequestsAndFourResponses()
    {
        var directory = Directory.CreateTempSubdirectory("crafting-revisions-").FullName;

        try {
            foreach (var file in Directory.GetFiles(HabbiconPacketTests.Repo("Resources/Revisions"), "*.json")) {
                File.Copy(file, Path.Join(directory, Path.GetFileName(file)));
            }

            var cache = new Plus.Communication.Revisions.RevisionsCache();
            typeof(Plus.Communication.Revisions.RevisionsCache).GetField("_directory",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(cache, directory);
            await cache.Start();

            foreach (var name in new[] { "NITRO-1-6-6", "NITRO-3-6-0", "OCTANE-3-6-0-FLOOR-20260909" }) {
                var revision = cache.Revisions[name];
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.CraftEvent, revision.IncomingIdToInternalIdMapping[3591]);
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.CraftSecretEvent, revision.IncomingIdToInternalIdMapping[1251]);
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.GetCraftableProductsEvent, revision.IncomingIdToInternalIdMapping[633]);
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.GetCraftingRecipeEvent, revision.IncomingIdToInternalIdMapping[1173]);
                Assert.Equal(Plus.Communication.Packets.Incoming.ClientPacketHeader.GetCraftingRecipesAvailableEvent, revision.IncomingIdToInternalIdMapping[3086]);
                Assert.Equal(1000u, revision.InternalIdToOutgoingIdMapping[Plus.Communication.Packets.Outgoing.ServerPacketHeader.CraftableProductsComposer]);
                Assert.Equal(2774u, revision.InternalIdToOutgoingIdMapping[Plus.Communication.Packets.Outgoing.ServerPacketHeader.CraftingRecipeComposer]);
                Assert.Equal(618u, revision.InternalIdToOutgoingIdMapping[Plus.Communication.Packets.Outgoing.ServerPacketHeader.CraftingResultComposer]);
                Assert.Equal(2124u, revision.InternalIdToOutgoingIdMapping[Plus.Communication.Packets.Outgoing.ServerPacketHeader.CraftingRecipesAvailableComposer]);
            }
        }
        finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class Recorder : ICraftingService
    {
        public List<string> Calls { get; } = [];
        public void GetProducts(GameClient session, uint altarId) => Calls.Add($"products:{altarId}");
        public void GetRecipe(GameClient session, string code) => Calls.Add($"recipe:{code}");
        public void GetAvailable(GameClient session, uint altarId, IReadOnlyList<uint> ids) => Calls.Add($"available:{altarId}:{string.Join(',', ids)}");
        public void Craft(GameClient session, uint altarId, string code) => Calls.Add($"craft:{altarId}:{code}");
        public void CraftSecret(GameClient session, uint altarId, IReadOnlyList<uint> ids) => Calls.Add($"secret:{altarId}:{string.Join(',', ids)}");
    }
}
