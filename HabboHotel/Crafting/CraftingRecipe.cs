using System.Collections.Immutable;

namespace Plus.HabboHotel.Crafting;

public sealed record CraftingIngredient(uint ItemId, int Amount);

public sealed record CraftingRecipe(int Id, string Code, string ProductCode, uint RewardId, bool Secret,
    bool Discovered, int? Remaining, string Achievement, ImmutableArray<CraftingIngredient> Ingredients)
{
    public bool Available => Remaining is null or > 0;

    public bool Matches(IReadOnlyDictionary<uint, int> selection) =>
        selection.Count == Ingredients.Length && Ingredients.All(ingredient =>
            selection.TryGetValue(ingredient.ItemId, out var amount) && amount == ingredient.Amount);

    public bool Contains(IReadOnlyDictionary<uint, int> selection) =>
        selection.All(pair => Ingredients.Any(ingredient => ingredient.ItemId == pair.Key && ingredient.Amount >= pair.Value));
}

public sealed record CraftedItem(uint ItemId, CraftingRecipe Recipe, bool Discovered);
