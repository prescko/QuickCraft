using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace QuickCraft;

internal static class RecipeActionLinks
{
    public static RichTextComponentBase Create(ICoreClientAPI api, string label, string langCode, double[] color, Action action)
    {
        return new RecipeActionButtonComponent(api, label, langCode, color, () => RunSafely(api, action));
    }

    private static void RunSafely(ICoreClientAPI api, Action action)
    {
        if (!HandbookSpaceCrafting.CanUseCraftingActions(api))
        {
            return;
        }

        try
        {
            action();
        }
        catch (Exception exception)
        {
            api.Logger.Error("Quick Craft action failed: {0}", exception);
            api.Gui.PlaySound("menubutton_wood", false, 1f);
            api.TriggerIngameError(typeof(RecipeActionLinks), "quickcraft-actionfailed", Lang.Get(ModIds.ModId + ":actionfailed"));
        }
    }
}
