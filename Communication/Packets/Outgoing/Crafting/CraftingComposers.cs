using System.Collections.Immutable;
using Plus.HabboHotel.GameClients;

namespace Plus.Communication.Packets.Outgoing.Crafting;

public sealed record CraftingProduct(string RecipeCode, string ProductCode, string FurnitureClassName)
{
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteString(RecipeCode);

        packet.WriteString(ProductCode);

        packet.WriteString(FurnitureClassName);
    }
}

public sealed class CraftableProductsComposer(ImmutableArray<CraftingProduct> products, ImmutableArray<string> ingredients) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CraftableProductsComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(products.Length);

        foreach (var product in products) {
            product.Compose(packet);
        }

        packet.WriteInteger(ingredients.Length);

        foreach (var ingredient in ingredients) {
            packet.WriteString(ingredient);
        }
    }
}

public sealed class CraftingRecipeComposer(ImmutableArray<(int Amount, string FurnitureClassName)> ingredients) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CraftingRecipeComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(ingredients.Length);

        foreach (var ingredient in ingredients) {
            packet.WriteInteger(ingredient.Amount);
            packet.WriteString(ingredient.FurnitureClassName);
        }
    }
}

public sealed class CraftingRecipesAvailableComposer(int count, bool complete) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CraftingRecipesAvailableComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteInteger(count);
        packet.WriteBoolean(complete);
    }
}

public sealed class CraftingResultComposer(CraftingProduct? product) : IServerPacket
{
    public uint MessageId => ServerPacketHeader.CraftingResultComposer;
    public void Compose(IOutgoingPacket packet)
    {
        packet.WriteBoolean(product is not null);
        product?.Compose(packet);
    }
}
