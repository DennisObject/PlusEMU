using System.Collections.Immutable;
using System.Data.Common;
using Microsoft.Extensions.Logging;
using Plus.Communication.Packets.Outgoing.Crafting;
using Plus.Communication.Packets.Outgoing.Inventory.Furni;
using Plus.HabboHotel.Achievements;
using Plus.HabboHotel.GameClients;
using Plus.HabboHotel.Items;
using Plus.HabboHotel.Items.DataFormat;
using Plus.HabboHotel.Users.Inventory.Furniture;
using Plus.Utilities.DependencyInjection;

namespace Plus.HabboHotel.Crafting;

[Singleton]
public interface ICraftingService
{
    void GetProducts(GameClient session, uint altarId);
    void GetRecipe(GameClient session, string code);
    void GetAvailable(GameClient session, uint altarId, IReadOnlyList<uint> itemIds);
    void Craft(GameClient session, uint altarId, string code);
    void CraftSecret(GameClient session, uint altarId, IReadOnlyList<uint> itemIds);
}

public sealed class CraftingService(ICraftingStore store, IItemDataManager definitions, IAchievementManager achievements,
    ILogger<CraftingService> logger) : ICraftingService
{
    public void GetProducts(GameClient session, uint altarId)
    {
        var altar = GetAltar(session, altarId);
        CraftingRecipe[] recipes = altar is null ? [] : store.Load(altar.Definition.Id, session.GetHabbo().Id).Where(recipe => Valid(recipe) && recipe.Available).ToArray();
        var products = recipes.Where(recipe => !recipe.Secret || recipe.Discovered).Select(Product).ToImmutableArray();
        var ingredients = recipes.SelectMany(recipe => recipe.Ingredients).Select(ingredient => definitions.Items[ingredient.ItemId].ItemName)
            .Distinct(StringComparer.Ordinal).ToImmutableArray();
        session.Send(new CraftableProductsComposer(products, ingredients, NativeProducts(session)));
    }

    public void GetRecipe(GameClient session, string code)
    {
        var recipe = store.Find(code, session.GetHabbo().Id);
        session.Send(new CraftingRecipeComposer(recipe is not null && Valid(recipe) && (!recipe.Secret || recipe.Discovered)
            ? recipe.Ingredients.Select(ingredient => (ingredient.Amount, definitions.Items[ingredient.ItemId].ItemName)).ToImmutableArray()
            : []));
    }

    public void GetAvailable(GameClient session, uint altarId, IReadOnlyList<uint> itemIds)
    {
        var altar = GetAltar(session, altarId);
        var selected = Select(session, itemIds);
        if (altar is null || selected is null) {
            session.Send(new CraftingRecipesAvailableComposer(0, false));
            return;
        }

        var counts = Counts(selected);
        var recipes = store.Load(altar.Definition.Id, session.GetHabbo().Id)
            .Where(recipe => Valid(recipe) && recipe.Available).ToArray();
        var complete = recipes.Any(recipe => recipe.Matches(counts));
        var undiscovered = recipes.Count(recipe => recipe.Secret && !recipe.Discovered && recipe.Contains(counts) && !recipe.Matches(counts));
        session.Send(new CraftingRecipesAvailableComposer(undiscovered, complete));
    }

    public void Craft(GameClient session, uint altarId, string code) => Execute(session, altarId, code, null);
    public void CraftSecret(GameClient session, uint altarId, IReadOnlyList<uint> itemIds) => Execute(session, altarId, null, itemIds);

    private void Execute(GameClient session, uint altarId, string? code, IReadOnlyList<uint>? itemIds)
    {
        var habbo = session.GetHabbo();
        CraftedItem? completed = null;
        lock (habbo.WalletSync) {
            var altar = GetAltar(session, altarId);
            var room = habbo.CurrentRoom;
            if (habbo.WalletClosed || !ReferenceEquals(habbo.Client, session) || room is null || altar is null
                || altar.OwnerId != (uint)habbo.Id || room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id)?.IsTrading != false) {
                session.Send(new CraftingResultComposer(null));
                return;
            }

            var recipes = store.Load(altar.Definition.Id, habbo.Id).Where(recipe => Valid(recipe) && recipe.Available).ToArray();
            var selected = itemIds is null ? null : Select(session, itemIds);
            var recipe = itemIds is null
                ? recipes.SingleOrDefault(recipe => recipe.Code == code && (!recipe.Secret || recipe.Discovered))
                : selected is null ? null : recipes.FirstOrDefault(recipe => recipe.Matches(Counts(selected)));
            if (recipe is null) {
                session.Send(new CraftingResultComposer(null));
                return;
            }

            selected ??= recipe.Ingredients.SelectMany(ingredient => habbo.Inventory.Furniture.GetItems
                .Where(item => item.Definition.Id == ingredient.ItemId && item.OwnerId == (uint)habbo.Id && item.UniqueNumber == 0 && item.UniqueSeries == 0)
                .OrderBy(item => item.Id).Take(ingredient.Amount)).ToArray();
            if (!recipe.Matches(Counts(selected))) {
                session.Send(new CraftingResultComposer(null));
                return;
            }

            var reserved = new List<InventoryItem>(selected.Length);
            try {
                foreach (var item in selected.OrderBy(item => item.Id)) {
                    if (!item.TryReserve()) {
                        session.Send(new CraftingResultComposer(null));
                        return;
                    }
                    reserved.Add(item);
                    if (!ReferenceEquals(habbo.Inventory.Furniture.GetItem(item.Id), item)) {
                        session.Send(new CraftingResultComposer(null));
                        return;
                    }
                }

                CraftedItem? committed;
                try {
                    committed = store.Craft(habbo.Id, room.RoomId, altar.Id, recipe, selected.Select(item => item.Id).ToArray(), itemIds is not null);
                }
                catch (DbException exception) {
                    logger.LogError(exception, "Failed to craft recipe {RecipeId} for user {UserId}", recipe.Id, habbo.Id);
                    session.Send(new CraftingResultComposer(null));
                    return;
                }
                if (committed is null) {
                    session.Send(new CraftingResultComposer(null));
                    return;
                }

                var reward = new InventoryItem { Id = committed.ItemId, OwnerId = (uint)habbo.Id,
                    Definition = definitions.Items[committed.Recipe.RewardId], ExtraData = FurniObjectData.Empty };
                foreach (var item in selected) {
                    habbo.Inventory.Furniture.RemoveItem(item.Id);
                }
                if (!habbo.Inventory.Furniture.AddItem(reward)) {
                    throw new InvalidOperationException("Committed crafting reward was already present in inventory.");
                }

                // Publish the complete inventory mutation before any send can synchronously disconnect.
                foreach (var item in selected) {
                    session.Send(new FurniListRemoveComposer(item.Id));
                }
                session.Send(new FurniListAddComposer(InventoryItemSnapshot.Capture(reward)));
                session.Send(new FurniListNotificationComposer(reward.Id, reward.IsFloorItem ? 1 : 2));
                session.Send(new CraftingResultComposer(Product(committed.Recipe), NativeProducts(session)));
                session.Send(new FurniListUpdateComposer());
                completed = committed;
            }
            finally {
                foreach (var item in reserved) {
                    item.ReleaseReservation();
                }
            }
        }
        // Badge awards can wait on asynchronous sends; never run them while holding the wallet monitor.
        if (completed is not null && Active(session, habbo)) {
            achievements.ProgressAchievement(session, "ACH_Atcg", 1);
            if (completed.Discovered && Active(session, habbo)) {
                achievements.ProgressAchievement(session, "ACH_AtcgSecret", 1);
            }
            if (completed.Recipe.Achievement.Length > 0 && Active(session, habbo)) {
                achievements.ProgressAchievement(session, completed.Recipe.Achievement, 1);
            }
        }
    }

    private static Item? GetAltar(GameClient session, uint altarId)
    {
        var habbo = session.GetHabbo();
        var room = habbo.CurrentRoom;
        if (room is null || room.GetRoomUserManager().GetRoomUserByHabbo(habbo.Id) is null) {
            return null;
        }
        var item = room.GetRoomItemHandler().GetItem(altarId);
        return item is { IsTemporary: false } && item.RoomId == room.RoomId && ReferenceEquals(item.GetRoom(), room) ? item : null;
    }

    private static InventoryItem[]? Select(GameClient session, IReadOnlyList<uint> itemIds)
    {
        if (itemIds.Count > 50 || itemIds.Any(id => id == 0) || itemIds.Distinct().Count() != itemIds.Count) {
            return null;
        }
        var habbo = session.GetHabbo();
        var selected = itemIds.Select(habbo.Inventory.Furniture.GetItem).ToArray();
        return selected.Any(item => item is null || item.OwnerId != (uint)habbo.Id || item.UniqueNumber != 0 || item.UniqueSeries != 0) ? null : selected.Select(item => item!).ToArray();
    }

    private static bool Active(GameClient session, Plus.HabboHotel.Users.Habbo habbo) =>
        !habbo.WalletClosed && ReferenceEquals(session.GetHabbo(), habbo) && ReferenceEquals(habbo.Client, session);
    private static bool NativeProducts(GameClient session) => session.Revision?.Name is not
        ("NITRO-1-6-6" or "NITRO-3-6-0" or "OCTANE-3-6-0-FLOOR-20260909");

    private bool Valid(CraftingRecipe recipe) => definitions.Items.TryGetValue(recipe.RewardId, out var reward)
        && reward.ProductType is "s" or "i" && recipe.Ingredients.All(ingredient => definitions.Items.ContainsKey(ingredient.ItemId));
    private CraftingProduct Product(CraftingRecipe recipe) => new(recipe.Code, recipe.ProductCode, definitions.Items[recipe.RewardId].ItemName);
    private static Dictionary<uint, int> Counts(IEnumerable<InventoryItem> selected) =>
        selected.GroupBy(item => item.Definition.Id).ToDictionary(group => group.Key, group => group.Count());
}
