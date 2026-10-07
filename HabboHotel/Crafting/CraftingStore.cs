using System.Collections.Immutable;
using System.Data;
using Dapper;
using Plus.Database;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Crafting;

[Singleton]
public interface ICraftingStore
{
    ImmutableArray<CraftingRecipe> Load(uint altarDefinitionId, int userId);
    CraftingRecipe? Find(string code, int userId);
    CraftedItem? Craft(int userId, uint roomId, uint altarId, CraftingRecipe recipe, IReadOnlyList<uint> itemIds, bool secretCraft);
}

public sealed class CraftingStore(IDatabase database) : ICraftingStore
{
    private const string RecipeColumns = "r.id,r.code,r.product_code AS ProductCode,r.reward_item_id AS RewardId,r.secret,r.remaining,r.achievement,EXISTS(SELECT 1 FROM user_crafting_recipes d WHERE d.user_id=@userId AND d.recipe_id=r.id) AS Discovered";

    public ImmutableArray<CraftingRecipe> Load(uint altarDefinitionId, int userId)
    {
        using var connection = database.Connection();
        var rows = connection.Query<RecipeRow>("SELECT " + RecipeColumns +
            " FROM crafting_recipes r INNER JOIN crafting_altars_recipes a ON a.recipe_id=r.id WHERE a.altar_item_id=@altarDefinitionId AND r.enabled=1 ORDER BY r.id",
            new { altarDefinitionId, userId }).ToArray();
        return ReadRecipes(connection, rows).ToImmutableArray();
    }

    public CraftingRecipe? Find(string code, int userId)
    {
        using var connection = database.Connection();
        var row = connection.QuerySingleOrDefault<RecipeRow>("SELECT " + RecipeColumns +
            " FROM crafting_recipes r WHERE r.code=@code AND r.enabled=1", new { code, userId });
        return row is null ? null : ReadRecipes(connection, [row]).SingleOrDefault();
    }

    public CraftedItem? Craft(int userId, uint roomId, uint altarId, CraftingRecipe recipe, IReadOnlyList<uint> itemIds, bool secretCraft)
    {
        if (itemIds.Count is < 1 or > 50 || itemIds.Distinct().Count() != itemIds.Count || itemIds.Contains(altarId)) {
            return null;
        }

        using var connection = database.Connection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        if (connection.ExecuteScalar<int?>("SELECT id FROM users WHERE id=@userId FOR UPDATE", new { userId }, transaction) is null) {
            return null;
        }

        var altar = connection.QuerySingleOrDefault<ItemRow>(
            "SELECT id,base_item AS ItemId FROM items WHERE id=@altarId AND user_id=@userId AND room_id=@roomId FOR UPDATE",
            new { altarId, userId, roomId }, transaction);
        if (altar is null || roomId == 0) {
            return null;
        }

        var row = connection.QuerySingleOrDefault<RecipeRow>("SELECT " + RecipeColumns +
            " FROM crafting_recipes r WHERE r.id=@id AND r.enabled=1 FOR UPDATE", new { id = recipe.Id, userId }, transaction);
        if (row is null || row.RewardId != recipe.RewardId || row.Code != recipe.Code || row.ProductCode != recipe.ProductCode
            || connection.ExecuteScalar<int>("SELECT COUNT(*) FROM crafting_altars_recipes WHERE altar_item_id=@itemId AND recipe_id=@id",
                new { itemId = altar.ItemId, id = recipe.Id }, transaction) != 1) {
            return null;
        }

        var current = ReadRecipes(connection, [row], transaction).SingleOrDefault();
        if (current is null || !current.Available || (!secretCraft && current.Secret && !current.Discovered)) {
            return null;
        }

        var selected = connection.Query<ItemRow>(
            "SELECT id,base_item AS ItemId FROM items WHERE id IN @itemIds AND user_id=@userId AND room_id=0 ORDER BY id FOR UPDATE",
            new { itemIds, userId }, transaction).ToArray();
        if (selected.Length != itemIds.Count || !current.Matches(selected.GroupBy(item => item.ItemId).ToDictionary(group => group.Key, group => group.Count()))) {
            return null;
        }

        if (current.Remaining is not null && connection.Execute(
            "UPDATE crafting_recipes SET remaining=remaining-1 WHERE id=@id AND remaining>0", new { id = current.Id }, transaction) != 1) {
            return null;
        }

        var removed = connection.Execute("DELETE FROM items WHERE id IN @itemIds AND user_id=@userId AND room_id=0", new { itemIds, userId }, transaction);
        if (removed != itemIds.Count) {
            throw new DBConcurrencyException("Crafting ingredients changed during consumption.");
        }

        connection.Execute("INSERT INTO items(base_item,user_id,room_id,x,y,z,wall_pos,rot,extra_data,limited_number,limited_stack) VALUES(@rewardId,@userId,0,0,0,0,'',0,'',0,0)",
            new { rewardId = current.RewardId, userId }, transaction);
        var id = connection.ExecuteScalar<uint>("SELECT LAST_INSERT_ID()", transaction: transaction);
        var discovered = current.Secret && connection.Execute(
            "INSERT IGNORE INTO user_crafting_recipes(user_id,recipe_id) VALUES(@userId,@recipeId)", new { userId, recipeId = current.Id }, transaction) == 1;
        transaction.Commit();
        return new(id, current, discovered);
    }

    private static IEnumerable<CraftingRecipe> ReadRecipes(IDbConnection connection, RecipeRow[] rows, IDbTransaction? transaction = null)
    {
        if (rows.Length == 0) {
            return [];
        }

        var ingredients = connection.Query<IngredientRow>(
            "SELECT recipe_id AS RecipeId,item_id AS ItemId,amount FROM crafting_recipes_ingredients WHERE recipe_id IN @ids ORDER BY recipe_id,item_id" + (transaction is null ? "" : " FOR UPDATE"),
            new { ids = rows.Select(row => row.Id).ToArray() }, transaction).ToLookup(row => row.RecipeId);
        return rows.Select(row => new CraftingRecipe(row.Id, row.Code, row.ProductCode, row.RewardId, row.Secret,
            row.Discovered, row.Remaining, row.Achievement,
            ingredients[row.Id].Select(ingredient => new CraftingIngredient(ingredient.ItemId, ingredient.Amount)).ToImmutableArray()))
            .Where(recipe => recipe.Ingredients.Length > 0 && recipe.Ingredients.All(ingredient => ingredient.Amount > 0)
                && recipe.Ingredients.Sum(ingredient => (long)ingredient.Amount) <= 50);
    }

    private sealed class RecipeRow
    {
        public int Id { get; set; }
        public string Code { get; set; } = "";
        public string ProductCode { get; set; } = "";
        public uint RewardId { get; set; }
        public bool Secret { get; set; }
        public bool Discovered { get; set; }
        public int? Remaining { get; set; }
        public string Achievement { get; set; } = "";
    }

    private sealed class IngredientRow
    {
        public int RecipeId { get; set; }
        public uint ItemId { get; set; }
        public int Amount { get; set; }
    }

    private sealed class ItemRow
    {
        public uint Id { get; set; }
        public uint ItemId { get; set; }
    }
}
