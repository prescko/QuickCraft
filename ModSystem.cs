using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace QuickHandbookCraft;

public sealed class QuickHandbookCraftModSystem : ModSystem
{
    private Harmony? harmony;

    public override bool ShouldLoad(EnumAppSide side)
    {
        return side == EnumAppSide.Client;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        HandbookSpaceCrafting.SetApi(api);
        harmony = new Harmony(ModIds.HarmonyId);
        harmony.PatchAll();
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(ModIds.HarmonyId);
        harmony = null;
        HandbookSpaceCrafting.SetApi(null);
    }
}

internal static class ModIds
{
    public const string HarmonyId = "quickhandbookcraft.spacecraft";
}
