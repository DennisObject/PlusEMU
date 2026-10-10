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

    [Fact]
    public void ProductLayoutLeavesTheFollowingIngredientCountAtTheCorrectOffset()
    {
        using var stream = Plus.Communication.Flash.PlusMemoryStream.GetStream();
        var outgoing = new Plus.Communication.Flash.FlashOutgoingPacket(stream);
        var product = new CraftingProduct("recipe", "product", "furniture");
        new CraftableProductsComposer([product], ["ingredient"]).Compose(outgoing);
        var incoming = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = stream.ToArray()[6..] };
        Assert.Equal(1, incoming.ReadInt());
        Assert.Equal("recipe", incoming.ReadString());

        Assert.Equal("product", incoming.ReadString());

        Assert.Equal("furniture", incoming.ReadString());
        Assert.Equal(1, incoming.ReadInt());
        Assert.Equal("ingredient", incoming.ReadString());
        Assert.False(incoming.HasDataRemaining());
        var result = new HabbiconTestSupport.RecordingPacket();
        new CraftingResultComposer(product).Compose(result);
        Assert.Equal(new object[] { true, "recipe", "product", "furniture" }, result.Writes);
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

    [Fact]
    public async Task AirCraftingIdsDispatchAltarAndRecipePayloadsToTheirOwnOperations()
    {
        var service = new Recorder();
        var (client, _) = HabbiconTestSupport.Client(new Habbo { Id = 7 });
        using var manager = new Plus.Communication.Packets.PacketManager(
            [new GetCraftableProductsEvent(service), new GetCraftingRecipeEvent(service)],
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Plus.Communication.Packets.PacketManager>.Instance);
        using var altarStream = Plus.Communication.Flash.PlusMemoryStream.GetStream();
        new Plus.Communication.Flash.FlashOutgoingPacket(altarStream).WriteInt(100_000);
        var altar = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = altarStream.ToArray()[6..] };
        using var recipeStream = Plus.Communication.Flash.PlusMemoryStream.GetStream();
        new Plus.Communication.Flash.FlashOutgoingPacket(recipeStream).WriteString("recipe");
        var recipe = new Plus.Communication.Flash.FlashIncomingPacket { Buffer = recipeStream.ToArray()[6..] };

        // CraftingWidgetHandler sends class_3258(altar) on 2698 and class_3259(recipe) on 1420.
        await manager.TryExecutePacket(client, 2698, altar);
        await manager.TryExecutePacket(client, 1420, recipe);

        Assert.Equal(new[] { "products:100000", "recipe:recipe" }, service.Calls);
        Assert.False(altar.HasDataRemaining());
        Assert.False(recipe.HasDataRemaining());
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
