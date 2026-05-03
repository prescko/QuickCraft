using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace QuickHandbookCraft;

internal static class RecipeActionLinks
{
    public static LinkTextComponent Create(ICoreClientAPI api, string label, string langCode, Action action)
    {
        CairoFont font = CairoFont.WhiteMediumText().WithColor(new double[] { 1.0, 0.92, 0.72, 1 });
        LinkTextComponent link = new(api, " " + label + " ", font, _ => RunSafely(api, action))
        {
            Clickable = true,
            PaddingLeft = 6,
            PaddingRight = 6,
            VerticalAlign = (EnumVerticalAlign)3
        };

        link.SetHref("quickhandbookcraft:" + langCode);
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
            api.Logger.Error("Quick Handbook Craft action failed: {0}", exception);
            api.Gui.PlaySound("menubutton_wood", false, 1f);
            api.TriggerIngameError(typeof(RecipeActionLinks), "quickhandbookcraft-actionfailed", Lang.Get("quickhandbookcraft:actionfailed"));
        }
    }
}
