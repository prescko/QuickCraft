using System.Reflection;
using Vintagestory.API.Client;

namespace QuickCraft;

internal static class SlideshowRecipeReader
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static GridRecipeAndUnnamedIngredients[] GetRecipeGroup(SlideshowGridRecipeTextComponent component)
    {
        object? value = ReadMember(component, "GridRecipesAndUnnamedIngredients")
            ?? ReadMember(component, "GridRecipesAndUnIn");

        return value as GridRecipeAndUnnamedIngredients[] ?? Array.Empty<GridRecipeAndUnnamedIngredients>();
    }

    private static object? ReadMember(object instance, string name)
    {
        Type type = instance.GetType();
        return type.GetField(name, Instance)?.GetValue(instance)
            ?? type.GetProperty(name, Instance)?.GetValue(instance);
    }
}
