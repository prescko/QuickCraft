using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace QuickCraft;

#pragma warning disable CS0618
internal enum CraftFillResult
{
    Success,
    AlreadyFull,
    MissingItems
}

internal static class RecipeGridFiller
{
    public static CraftFillResult TryFillOne(ICoreClientAPI api, GridRecipe[] recipes)
    {
        return TryFill(api, recipes, max: false);
    }

    public static CraftFillResult TryFillMax(ICoreClientAPI api, GridRecipe[] recipes)
    {
        return TryFill(api, recipes, max: true);
    }

    public static CraftFillResult TryFill(ICoreClientAPI api, GridRecipe[] recipes, bool max)
    {
        recipes = recipes?.Where(recipe => recipe != null).ToArray() ?? Array.Empty<GridRecipe>();
        if (recipes.Length == 0)
        {
            return CraftFillResult.MissingItems;
        }

        IClientPlayer player = api.World.Player;
        IPlayerInventoryManager manager = player.InventoryManager;
        IInventory? crafting = manager.GetOwnInventory("craftinggrid");
        IInventory? backpack = manager.GetOwnInventory("backpack");
        IInventory? hotbar = manager.GetHotbarInventory();

        if (crafting == null || backpack == null || hotbar == null)
        {
            return CraftFillResult.MissingItems;
        }

        ItemSlot[] input = crafting.Take(9).ToArray();
        IEnumerable<ItemSlot> openedContainers = api.Input.ShiftHeld()
            ? Enumerable.Empty<ItemSlot>()
            : manager.OpenedInventories.OfType<InventoryGeneric>().SelectMany(inventory => inventory);

        List<ItemSlot> stacks = backpack
            .Concat(hotbar)
            .Where(slot => slot is not ItemSlotBackpack)
            .Concat(openedContainers)
            .ToList();

        Dictionary<AssetLocation, int> available = stacks
            .Concat(input)
            .Where(slot => slot != null && !slot.Empty && slot.Itemstack?.Collectible?.Code != null)
            .GroupBy(slot => slot.Itemstack!.Collectible.Code)
            .ToDictionary(group => group.Key, group => group.Sum(slot => slot.StackSize));

        Dictionary<string, int> wildcards = recipes
            .SelectMany(GetRecipeIngredients)
            .Where(ingredient => ingredient.IsWildCard && ingredient.Code != null)
            .Select(ingredient => new IngredientCode(ingredient))
            .DistinctBy(code => code.Key)
            .ToDictionary(code => code.Key, code => available.Sum(item => code.Matches(item.Key) ? item.Value : 0));

        GridRecipe[] candidates = recipes
            .Where(candidate => SafeMatches(candidate, player, api.World, input) || CanMake(candidate, input, stacks, available, wildcards))
            .OrderByDescending(candidate => SafeMatches(candidate, player, api.World, input) ? int.MaxValue : ScoreRecipe(candidate, available))
            .ToArray();

        if (candidates.Length == 0)
        {
            return CraftFillResult.MissingItems;
        }

        foreach (GridRecipe recipe in candidates)
        {
            bool changed = false;
            bool lastChanged;

            do
            {
                lastChanged = AddIngredients(api, input, recipe, stacks);
                changed |= lastChanged;
            }
            while (max && lastChanged);

            if (changed)
            {
                return CraftFillResult.Success;
            }

            if (SafeMatches(recipe, player, api.World, input))
            {
                return CraftFillResult.AlreadyFull;
            }
        }

        return recipes.Any(candidate => SafeMatches(candidate, player, api.World, input))
            ? CraftFillResult.AlreadyFull
            : CraftFillResult.MissingItems;
    }

    private static bool SafeMatches(GridRecipe recipe, IPlayer player, IWorldAccessor world, ItemSlot[] input)
    {
        try
        {
            return recipe.Matches(player, world, input, 3);
        }
        catch
        {
            return false;
        }
    }

    private static bool CanMake(
        GridRecipe recipe,
        ItemSlot[] input,
        List<ItemSlot> stacks,
        Dictionary<AssetLocation, int> available,
        Dictionary<string, int> wildcards)
    {
        CraftingRecipeIngredient[] ingredients = GetResolvedIngredients(recipe)
            .Where(ingredient => ingredient != null && ingredient.Code != null)
            .ToArray();

        if (ingredients.Length == 0)
        {
            return false;
        }

        bool possible = ingredients
            .GroupBy(ingredient => new IngredientCode(ingredient))
            .All(group => (group.Key.Wild ? wildcards.GetValueOrDefault(group.Key.Key) : available.GetValueOrDefault(group.Key.Code!)) >= group.Sum(ingredient => ingredient.Quantity));

        if (!possible || !ingredients.Any(ingredient => ingredient.IsWildCard || ingredient.IsTool))
        {
            return possible;
        }

        Dictionary<ItemSlot, int> used = new();

        foreach (CraftingRecipeIngredient ingredient in ingredients.Where(ingredient => !ingredient.IsWildCard).Concat(ingredients.Where(ingredient => ingredient.IsWildCard)))
        {
            int need = ingredient.Quantity;

            foreach (ItemSlot slot in input.Concat(stacks))
            {
                if (!Satisfies(ingredient, slot.Itemstack))
                {
                    continue;
                }

                used.TryAdd(slot, 0);
                int use = Math.Min(need, slot.StackSize - used[slot]);
                used[slot] += use;
                need -= use;

                if (need == 0)
                {
                    break;
                }
            }

            if (need > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int ScoreRecipe(GridRecipe recipe, Dictionary<AssetLocation, int> available)
    {
        int score = 0;

        foreach (CraftingRecipeIngredient ingredient in GetResolvedIngredients(recipe).Where(ingredient => ingredient.Code != null))
        {
            if (!ingredient.IsWildCard)
            {
                score += available.GetValueOrDefault(ingredient.Code!);
                continue;
            }

            IngredientCode code = new(ingredient);
            int maxAvailable = available
                .Where(item => code.Matches(item.Key))
                .Select(item => item.Value)
                .DefaultIfEmpty(0)
                .Max();

            score += maxAvailable;
        }

        return score;
    }

    private static bool AddIngredients(ICoreClientAPI api, ItemSlot[] input, GridRecipe recipe, List<ItemSlot> available)
    {
        List<(ItemSlot From, ItemSlot To, int Quantity)> operations = new();
        Dictionary<ItemSlot, int> remaining = new();
        CraftingRecipeIngredient?[] ingredients = GetSlotIngredients(recipe);
        if (ingredients.Length == 0)
        {
            return false;
        }

        if (recipe.Shapeless)
        {
            input = input.ToArray();
            CraftingRecipeIngredient?[] shapelessIngredients = ingredients.Where(ingredient => ingredient != null).ToArray();
            ItemSlot?[] newInput = shapelessIngredients.Select(ingredient => PullFirst(input, slot => Satisfies(ingredient, slot?.Itemstack))).ToArray();

            foreach (ItemSlot slot in input.Where(slot => slot != null && !slot.Empty))
            {
                EmptySlot(api, slot);
            }

            for (int i = 0; i < newInput.Length; i++)
            {
                newInput[i] ??= PullFirst(input, slot => slot != null && slot.Empty);
            }

            input = newInput!;
            ingredients = shapelessIngredients;
        }
        else if (recipe.Width * recipe.Height < 9)
        {
            Bounds bounds = new(recipe.Width, recipe.Height, 3);

            for (int i = 8; i >= 0; i--)
            {
                if (input[i].Empty)
                {
                    continue;
                }

                for (int j = 0; j < ingredients.Length; j++)
                {
                    if (SatisfiesAt(j, i))
                    {
                        bounds.Align(i, j);
                        break;
                    }
                }
            }

            foreach (ItemSlot slot in input.Where((slot, index) => !bounds.Contains(index) ? slot.Itemstack != null : !SatisfiesAt(bounds.ToInner(index), index)))
            {
                EmptySlot(api, slot);
            }

            input = input.Where((_, index) => bounds.Contains(index)).ToArray();
        }
        else
        {
            foreach (ItemSlot slot in input.Where((_, index) => !SatisfiesAt(index, index)))
            {
                EmptySlot(api, slot);
            }
        }

        available = available
            .Where(slot => !slot.Empty)
            .OrderByDescending(slot => slot.StackSize)
            .ToList();

        int completeSets = input
            .Select((slot, index) => CurrentSets(GetIngredient(ingredients, index), slot))
            .Where(sets => sets >= 0)
            .DefaultIfEmpty(0)
            .Min();

        for (int i = 0; i < input.Length; i++)
        {
            CraftingRecipeIngredient? ingredient = GetIngredient(ingredients, i);
            if (ingredient == null)
            {
                continue;
            }

            ItemStack? stack = input[i].Itemstack;
            int need = ingredient.Quantity;

            if (stack != null)
            {
                int size = stack.StackSize;
                if (ingredient.IsTool)
                {
                    continue;
                }

                if (size > completeSets * need)
                {
                    need = (completeSets + 1) * need - size;
                    if (need <= 0)
                    {
                        continue;
                    }
                }

                if (stack.Collectible.MaxStackSize < size + need)
                {
                    return false;
                }
            }

            foreach (ItemSlot slot in available)
            {
                if (!Satisfies(ingredient, slot.Itemstack) || !input[i].CanTakeFrom(slot, EnumMergePriority.AutoMerge))
                {
                    continue;
                }

                if (!remaining.TryGetValue(slot, out int sizeLeft))
                {
                    sizeLeft = slot.Itemstack!.StackSize;
                    remaining[slot] = sizeLeft;
                }

                int take = Math.Min(sizeLeft, need);
                operations.Add((slot, input[i], take));
                remaining[slot] = sizeLeft - take;
                need -= take;

                if (need <= 0)
                {
                    break;
                }
            }

            if (need > 0)
            {
                return false;
            }
        }

        IClientPlayer player = api.World.Player;
        IPlayerInventoryManager manager = player.InventoryManager;
        bool changed = false;

        foreach ((ItemSlot from, ItemSlot to, int quantity) in operations)
        {
            ItemStackMoveOperation op = new(api.World, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, quantity)
            {
                ActingPlayer = player
            };

            int before = to.StackSize;
            object? packet = manager.TryTransferTo(from, to, ref op);

            if (to.StackSize > before)
            {
                SendPacket(api, packet);
                changed = true;
            }
        }

        return changed;

        bool SatisfiesAt(int ingredientIndex, int inputIndex)
        {
            return inputIndex >= 0
                && inputIndex < input.Length
                && Satisfies(GetIngredient(ingredients, ingredientIndex), input[inputIndex].Itemstack);
        }
    }

    private static int CurrentSets(CraftingRecipeIngredient? ingredient, ItemSlot slot)
    {
        if (ingredient == null)
        {
            return -1;
        }

        if (ingredient.IsTool)
        {
            return slot.StackSize <= 0 ? 0 : -1;
        }

        return ingredient.Quantity <= 0 ? -1 : slot.StackSize / ingredient.Quantity;
    }

    private static void EmptySlot(ICoreClientAPI api, ItemSlot slot)
    {
        IClientPlayer player = api.World.Player;
        IPlayerInventoryManager manager = player.InventoryManager;
        ItemStackMoveOperation op = new(api.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, slot.StackSize)
        {
            ActingPlayer = player
        };

        object[]? packets = manager.TryTransferAway(slot, ref op, false, false);
        if (packets != null)
        {
            foreach (object packet in packets)
            {
                SendPacket(api, packet);
            }
        }

        if (!slot.Empty)
        {
            manager.DropItem(slot, true);
        }
    }

    private static ItemSlot? PullFirst(ItemSlot?[] slots, System.Func<ItemSlot?, bool> test)
    {
        for (int i = 0; i < slots.Length; i++)
        {
            if (!test(slots[i]))
            {
                continue;
            }

            ItemSlot? result = slots[i];
            slots[i] = null;
            return result;
        }

        return null;
    }

    private static bool Satisfies(CraftingRecipeIngredient? ingredient, ItemStack? stack)
    {
        if (stack == null || stack.StackSize <= 0 || ingredient == null)
        {
            return false;
        }

        try
        {
            return ingredient.SatisfiesAsIngredient(stack, false)
                && (!ingredient.IsTool || stack.Collectible.GetRemainingDurability(stack) >= ingredient.ToolDurabilityCost);
        }
        catch
        {
            return false;
        }
    }

    private static CraftingRecipeIngredient[] GetRecipeIngredients(GridRecipe? recipe)
    {
        if (recipe?.Ingredients != null)
        {
            return recipe.Ingredients.Values
                .Where(ingredient => ingredient != null)
                .ToArray();
        }

        return GetResolvedIngredients(recipe);
    }

    private static CraftingRecipeIngredient[] GetResolvedIngredients(GridRecipe? recipe)
    {
        return recipe?.ResolvedIngredients?
            .Where(ingredient => ingredient != null)
            .Cast<CraftingRecipeIngredient>()
            .ToArray() ?? Array.Empty<CraftingRecipeIngredient>();
    }

    private static CraftingRecipeIngredient?[] GetSlotIngredients(GridRecipe? recipe)
    {
        return recipe?.ResolvedIngredients?.ToArray() ?? Array.Empty<CraftingRecipeIngredient?>();
    }

    private static CraftingRecipeIngredient? GetIngredient(CraftingRecipeIngredient?[] ingredients, int index)
    {
        return index >= 0 && index < ingredients.Length ? ingredients[index] : null;
    }

    private static void SendPacket(ICoreClientAPI api, object? packet)
    {
        InventoryPacketPatcher.Send(api, packet);
    }
}
