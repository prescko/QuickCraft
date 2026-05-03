using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace QuickHandbookCraft;

[HarmonyPatch]
public static class CreatedByInfoPatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        Type? type = AccessTools.TypeByName("Vintagestory.GameContent.CollectibleBehaviorHandbookTextAndExtraInfo");
        MethodInfo? method = type == null ? null : AccessTools.Method(type, "addCreatedByInfo");

        if (method != null)
        {
            yield return method;
        }
    }

    public static void Postfix(List<RichTextComponentBase>? components)
    {
        ICoreClientAPI? api = HandbookSpaceCrafting.ClientApi;
        if (api == null || components == null)
        {
            return;
        }

        for (int i = 0; i < components.Count; i++)
        {
            if (components[i] is not SlideshowGridRecipeTextComponent gridComponent)
            {
                continue;
            }

            GridRecipeAndUnnamedIngredients[] group = SlideshowRecipeReader.GetRecipeGroup(gridComponent);
            if (group.Length == 0)
            {
                continue;
            }

            GridRecipe[] recipes = group
                .Select(entry => entry.Recipe)
                .Where(recipe => recipe != null)
                .ToArray();

            if (recipes.Length == 0)
            {
                continue;
            }

            RichTextComponentBase[] buttons =
            {
                RecipeActionLinks.Create(api, "[ + ]", "addone", () => HandbookSpaceCrafting.TryFillRecipes(recipes, max: false)),
                RecipeActionLinks.Create(api, "[ * ]", "craftmax", () => HandbookSpaceCrafting.TryCraftRecipes(recipes)),
                RecipeActionLinks.Create(api, "[ ALL ]", "craftall", () => HandbookSpaceCrafting.TryCraftAllRecipes(recipes))
            };

            components.InsertRange(i + 1, buttons);
            i += buttons.Length;
        }
    }
}
