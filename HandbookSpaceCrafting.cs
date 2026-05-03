using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace QuickHandbookCraft;

internal static class HandbookSpaceCrafting
{
    private const int MaxCraftAllCycles = 512;

    private static ICoreClientAPI? api;
    private static bool craftAllRunning;

    public static ICoreClientAPI? ClientApi => api;

    public static void SetApi(ICoreClientAPI? clientApi)
    {
        api = clientApi;
    }

    public static bool TryFillRecipes(GridRecipe[] recipes, bool max)
    {
        ICoreClientAPI? capi = api;
        if (capi == null)
        {
            return false;
        }

        if (!HasCraftingGrid(capi, out _))
        {
            return true;
        }

        CraftFillResult result = RecipeGridFiller.TryFill(capi, recipes, max);
        return HandleFillResult(capi, result);
    }

    public static bool TryCraftRecipes(GridRecipe[] recipes)
    {
        ICoreClientAPI? capi = api;
        if (capi == null)
        {
            return false;
        }

        if (!HasCraftingGrid(capi, out IInventory? craftingGrid))
        {
            return true;
        }

        CraftFillResult fillResult = RecipeGridFiller.TryFillMax(capi, recipes);
        if (fillResult == CraftFillResult.Success || fillResult == CraftFillResult.AlreadyFull)
        {
            capi.Event.RegisterCallback(_ => TransferOutputWithRetries(capi, craftingGrid!, 8), 100);
            capi.Gui.PlaySound("menubutton_press", false, 1f);
            return true;
        }

        return HandleFillResult(capi, fillResult);
    }

    public static bool TryCraftAllRecipes(GridRecipe[] recipes)
    {
        ICoreClientAPI? capi = api;
        if (capi == null)
        {
            return false;
        }

        if (craftAllRunning)
        {
            return true;
        }

        if (!HasCraftingGrid(capi, out _))
        {
            return true;
        }

        craftAllRunning = true;
        CraftAllStep(capi, recipes, 0);
        return true;
    }

    private static bool HasCraftingGrid(ICoreClientAPI capi, out IInventory? craftingGrid)
    {
        craftingGrid = capi.World.Player.InventoryManager.GetOwnInventory("craftinggrid");

        if (craftingGrid != null && craftingGrid.Count >= 10)
        {
            return true;
        }

        capi.Gui.PlaySound("menubutton_wood", false, 1f);
        capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickhandbookcraft-nogrid", Lang.Get("quickhandbookcraft:nocraftinggrid"));
        return false;
    }

    private static bool HandleFillResult(ICoreClientAPI capi, CraftFillResult result)
    {
        switch (result)
        {
            case CraftFillResult.Success:
                capi.Gui.PlaySound("menubutton_press", false, 1f);
                return true;
            case CraftFillResult.AlreadyFull:
                return true;
            default:
                capi.Gui.PlaySound("menubutton_wood", false, 1f);
                capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickhandbookcraft-missing", Lang.Get("quickhandbookcraft:cannotfill"));
                return false;
        }
    }

    private static bool TryTransferOutput(ICoreClientAPI capi, IInventory craftingGrid)
    {
        ItemSlot outputSlot = craftingGrid[9];
        if (outputSlot == null || outputSlot.Empty || outputSlot.StackSize <= 0)
        {
            return false;
        }

        IClientPlayer player = capi.World.Player;
        IPlayerInventoryManager manager = player.InventoryManager;

        if (!manager.MouseItemSlot.Empty)
        {
            return false;
        }

        ItemStackMoveOperation op = new(capi.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, outputSlot.StackSize)
        {
            ActingPlayer = player
        };

        if (craftingGrid is not InventoryBase inventoryBase)
        {
            return false;
        }

        object? packet = inventoryBase.ActivateSlot(9, manager.MouseItemSlot, ref op);
        if (packet != null)
        {
            capi.Network.SendPacketClient(packet);
        }

        return op.MovedQuantity > 0 || packet != null;
    }

    private static void TransferOutputWithRetries(ICoreClientAPI capi, IInventory craftingGrid, int attemptsLeft)
    {
        TransferOutputWithRetries(capi, craftingGrid, attemptsLeft, _ => { });
    }

    private static void TransferOutputWithRetries(ICoreClientAPI capi, IInventory craftingGrid, int attemptsLeft, Action<bool> completed)
    {
        if (TryTransferOutput(capi, craftingGrid))
        {
            completed(true);
            return;
        }

        if (attemptsLeft <= 0)
        {
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickhandbookcraft-outputblocked", Lang.Get("quickhandbookcraft:outputblocked"));
            completed(false);
            return;
        }

        capi.Event.RegisterCallback(_ => TransferOutputWithRetries(capi, craftingGrid, attemptsLeft - 1, completed), 50);
    }

    private static void CraftAllStep(ICoreClientAPI capi, GridRecipe[] recipes, int cycles)
    {
        if (cycles >= MaxCraftAllCycles)
        {
            craftAllRunning = false;
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickhandbookcraft-craftalllimit", Lang.Get("quickhandbookcraft:craftalllimit"));
            return;
        }

        if (!HasCraftingGrid(capi, out IInventory? craftingGrid))
        {
            craftAllRunning = false;
            return;
        }

        CraftFillResult fillResult = RecipeGridFiller.TryFillMax(capi, recipes);
        if (fillResult != CraftFillResult.Success && fillResult != CraftFillResult.AlreadyFull)
        {
            craftAllRunning = false;
            if (cycles == 0)
            {
                HandleFillResult(capi, fillResult);
            }
            return;
        }

        if (cycles == 0)
        {
            capi.Gui.PlaySound("menubutton_press", false, 1f);
        }

        capi.Event.RegisterCallback(_ =>
        {
            TransferOutputWithRetries(capi, craftingGrid!, 8, moved =>
            {
                if (!moved)
                {
                    craftAllRunning = false;
                    return;
                }

                capi.Event.RegisterCallback(__ => CraftAllStep(capi, recipes, cycles + 1), 75);
            });
        }, 100);
    }
}
