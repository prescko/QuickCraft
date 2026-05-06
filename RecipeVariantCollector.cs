using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace QuickCraft;

internal static class RecipeVariantCollector
{
    private static readonly Dictionary<string, GridRecipe[]> recipesByOutput = new();

    public static void ClearCache()
    {
        recipesByOutput.Clear();
    }

    public static GridRecipe[] IncludeSameOutputVariants(ICoreClientAPI api, IEnumerable<GridRecipe?> recipes)
    {
        GridRecipe[] baseRecipes = recipes
            .Where(recipe => recipe != null)
            .Cast<GridRecipe>()
            .ToArray();

        if (baseRecipes.Length == 0)
        {
            return Array.Empty<GridRecipe>();
        }

        HashSet<GridRecipe> result = new(baseRecipes);
        foreach (string outputKey in baseRecipes.Select(GetOutputKey).Where(key => key != null).Cast<string>().Distinct())
        {
            foreach (GridRecipe recipe in GetRecipesForOutput(api, outputKey))
            {
                result.Add(recipe);
            }
        }

        return result.ToArray();
    }

    private static GridRecipe[] GetRecipesForOutput(ICoreClientAPI api, string outputKey)
    {
        if (recipesByOutput.TryGetValue(outputKey, out GridRecipe[]? cached))
        {
            return cached;
        }

        GridRecipe[] recipes = api.World.GridRecipes?
            .Where(recipe => GetOutputKey(recipe) == outputKey)
            .ToArray() ?? Array.Empty<GridRecipe>();

        recipesByOutput[outputKey] = recipes;
        return recipes;
    }

    private static string? GetOutputKey(GridRecipe? recipe)
    {
        CraftingRecipeIngredient? output = recipe?.Output;
        if (output == null)
        {
            return null;
        }

        ItemStack? stack = output.ResolvedItemStack;
        AssetLocation? code = stack?.Collectible?.Code ?? output.Code;
        if (code == null)
        {
            return null;
        }

        EnumItemClass itemClass = stack?.Class ?? output.Type;
        return itemClass + ":" + code;
    }
}
