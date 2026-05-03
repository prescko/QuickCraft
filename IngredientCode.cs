using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace QuickHandbookCraft;

internal readonly struct IngredientCode
{
    private readonly string[]? include;
    private readonly string[]? exclude;
    private readonly string? key;

    public readonly AssetLocation Code;
    public readonly bool Wild;

    public string Key => key ?? MakeKey();

    public IngredientCode(CraftingRecipeIngredient ingredient)
    {
        include = null;
        exclude = null;
        key = null;
        Code = ingredient.Code;
        Wild = ingredient.IsWildCard;

        if (Wild)
        {
            include = ingredient.AllowedVariants;
            exclude = ingredient.SkipVariants;
        }
    }

    public bool Matches(AssetLocation item)
    {
        if (!Wild)
        {
            return Code == item;
        }

        return WildcardUtil.Match(Code, item, include) && (exclude == null || !WildcardUtil.MatchesVariants(Code, item, exclude));
    }

    public override bool Equals(object? obj)
    {
        return obj is IngredientCode other && Key.Equals(other.Key, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        return Key.GetHashCode();
    }

    private string MakeKey()
    {
        StringBuilder builder = new();
        builder.Append(Code);
        AddArray(builder, include, '[');
        AddArray(builder, exclude, ']');
        return builder.ToString();
    }

    private static void AddArray(StringBuilder builder, string[]? values, char prefix)
    {
        if (values == null || values.Length == 0)
        {
            return;
        }

        builder.Append(prefix);
        builder.Append(values[0]);

        for (int i = 1; i < values.Length; i++)
        {
            builder.Append(',');
            builder.Append(values[i]);
        }
    }
}
