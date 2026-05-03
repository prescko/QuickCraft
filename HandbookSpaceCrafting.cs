using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace QuickCraft;

internal static class HandbookSpaceCrafting
{
    private const int MaxCraftAllCycles = 512;
    private const int OutputTransferAttempts = 8;
    private const int CraftAllOutputAttempts = 40;
    private const int CraftAllRetryDelayMs = 75;
    private const int CraftAllSettleDelayMs = 250;
    private const int CraftAllRefillDelayMs = 175;
    private const int CraftAllInputSettleAttempts = 16;
    private const int CraftAllInputSettleDelayMs = 75;

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
            capi.Event.RegisterCallback(_ => TransferOutputWithRetries(capi, craftingGrid!, OutputTransferAttempts), 100);
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
        capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-nogrid", Lang.Get(ModIds.ModId + ":nocraftinggrid"));
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
                capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-missing", Lang.Get(ModIds.ModId + ":cannotfill"));
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
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-outputblocked", Lang.Get(ModIds.ModId + ":outputblocked"));
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
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-craftalllimit", Lang.Get(ModIds.ModId + ":craftalllimit"));
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

        string inputSignatureBeforeCraft = GetInputSignature(craftingGrid!);

        capi.Event.RegisterCallback(_ =>
        {
            TransferOutputForCraftAll(capi, craftingGrid!, inputSignatureBeforeCraft, CraftAllOutputAttempts, moved =>
            {
                if (!moved)
                {
                    craftAllRunning = false;
                    return;
                }

                capi.Event.RegisterCallback(__ => CraftAllStep(capi, recipes, cycles + 1), CraftAllRefillDelayMs);
            });
        }, 100);
    }

    private static void TransferOutputForCraftAll(ICoreClientAPI capi, IInventory craftingGrid, string inputSignatureBeforeCraft, int attemptsLeft, Action<bool> completed)
    {
        if (TryTransferOutput(capi, craftingGrid))
        {
            capi.Event.RegisterCallback(_ => WaitForCraftAllOutputToClear(capi, craftingGrid, inputSignatureBeforeCraft, CraftAllOutputAttempts, completed), CraftAllSettleDelayMs);
            return;
        }

        if (attemptsLeft <= 0)
        {
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-outputblocked", Lang.Get(ModIds.ModId + ":outputblocked"));
            completed(false);
            return;
        }

        capi.Event.RegisterCallback(_ => TransferOutputForCraftAll(capi, craftingGrid, inputSignatureBeforeCraft, attemptsLeft - 1, completed), CraftAllRetryDelayMs);
    }

    private static void WaitForCraftAllOutputToClear(ICoreClientAPI capi, IInventory craftingGrid, string inputSignatureBeforeCraft, int attemptsLeft, Action<bool> completed)
    {
        if (IsOutputSlotEmpty(craftingGrid))
        {
            WaitForCraftAllInputsToSettle(capi, craftingGrid, inputSignatureBeforeCraft, CraftAllInputSettleAttempts, completed);
            return;
        }

        if (attemptsLeft <= 0)
        {
            capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-outputblocked", Lang.Get(ModIds.ModId + ":outputblocked"));
            completed(false);
            return;
        }

        if (TryTransferOutput(capi, craftingGrid))
        {
            capi.Event.RegisterCallback(_ => WaitForCraftAllOutputToClear(capi, craftingGrid, inputSignatureBeforeCraft, attemptsLeft - 1, completed), CraftAllSettleDelayMs);
            return;
        }

        capi.Event.RegisterCallback(_ => WaitForCraftAllOutputToClear(capi, craftingGrid, inputSignatureBeforeCraft, attemptsLeft - 1, completed), CraftAllRetryDelayMs);
    }

    private static void WaitForCraftAllInputsToSettle(ICoreClientAPI capi, IInventory craftingGrid, string inputSignatureBeforeCraft, int attemptsLeft, Action<bool> completed)
    {
        if (GetInputSignature(craftingGrid) != inputSignatureBeforeCraft || attemptsLeft <= 0)
        {
            completed(true);
            return;
        }

        capi.Event.RegisterCallback(_ => WaitForCraftAllInputsToSettle(capi, craftingGrid, inputSignatureBeforeCraft, attemptsLeft - 1, completed), CraftAllInputSettleDelayMs);
    }

    private static bool IsOutputSlotEmpty(IInventory craftingGrid)
    {
        ItemSlot outputSlot = craftingGrid[9];
        return outputSlot == null || outputSlot.Empty || outputSlot.StackSize <= 0;
    }

    private static string GetInputSignature(IInventory craftingGrid)
    {
        string[] parts = new string[9];

        for (int i = 0; i < parts.Length; i++)
        {
            ItemSlot slot = craftingGrid[i];
            ItemStack? stack = slot?.Itemstack;
            parts[i] = stack?.Collectible?.Code == null ? "-" : stack.Collectible.Code + ":" + stack.StackSize;
        }

        return string.Join("|", parts);
    }
}
