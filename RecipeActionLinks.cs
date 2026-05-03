using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace QuickCraft;

internal static class RecipeActionLinks
{
    public static LinkTextComponent Create(ICoreClientAPI api, string label, string langCode, double[] color, Action action)
    {
        CairoFont font = CairoFont.WhiteMediumText().WithColor(color);
        ((FontConfig)font).UnscaledFontsize = GuiElement.scaled(18.0);

        LinkTextComponent link = new(api, " " + label + " ", font, _ => RunSafely(api, action))
        {
            Clickable = true,
            PaddingLeft = 10,
            PaddingRight = 10,
            VerticalAlign = (EnumVerticalAlign)3
        };

        link.SetHref(ModIds.ModId + ":" + langCode);
        return link;
    }

    private static void RunSafely(ICoreClientAPI api, Action action)
    {
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
