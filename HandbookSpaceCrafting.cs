using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace QuickCraft;

internal static class HandbookSpaceCrafting
{
    private const int MaxCraftAllCycles = 512;
    private const int OutputTransferAttempts = 16;
    private const int OutputTransferRetryDelayMs = 25;

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

        if (!CanUseCraftingActions(capi))
        {
            return true;
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

        if (!CanUseCraftingActions(capi))
        {
            return true;
        }

        if (!HasCraftingGrid(capi, out IInventory? craftingGrid))
        {
            return true;
        }

        CraftFillResult fillResult = RecipeGridFiller.TryFillMax(capi, recipes);
        if (fillResult == CraftFillResult.Success || fillResult == CraftFillResult.AlreadyFull)
        {
            RequestOutputCraftAfterFill(capi, fillResult);
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

        if (!CanUseCraftingActions(capi))
        {
            return true;
        }

        if (!HasCraftingGrid(capi, out _))
        {
            return true;
        }

        craftAllRunning = true;
        try
        {
            return CraftAllImmediate(capi, recipes);
        }
        finally
        {
            craftAllRunning = false;
        }
    }

    public static bool CanUseCraftingActions(ICoreClientAPI capi)
    {
        IInventory? craftingGrid = capi.World.Player.InventoryManager.GetOwnInventory("craftinggrid");
        return craftingGrid != null && craftingGrid.Count >= 10;
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

    private static bool TryTransferOutput(ICoreClientAPI capi, IInventory craftingGrid, bool requireOutputReady = false)
    {
        RefreshCraftingOutput(craftingGrid);

        ItemSlot outputSlot = craftingGrid[9];
        if (requireOutputReady && (outputSlot == null || outputSlot.Empty || outputSlot.StackSize <= 0))
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
        SendPacket(capi, packet);
        return packet != null || op.MovedQuantity > 0;
    }

    private static void TransferOutputWithRetries(ICoreClientAPI capi, IInventory craftingGrid, int attemptsLeft, Action<bool> completed)
    {
        if (!CanUseCraftingActions(capi))
        {
            completed(false);
            return;
        }

        if (TryTransferOutput(capi, craftingGrid))
        {
            completed(true);
            return;
        }

        if (attemptsLeft <= 0)
        {
            completed(false);
            return;
        }

        RegisterMenuCallback(capi, _ => TransferOutputWithRetries(capi, craftingGrid, attemptsLeft - 1, completed), OutputTransferRetryDelayMs);
    }

    private static void SendPacket(ICoreClientAPI capi, object? packet)
    {
        InventoryPacketPatcher.Send(capi, packet);
    }

    private static void RequestOutputCraftAfterFill(ICoreClientAPI capi, CraftFillResult fillResult)
    {
        capi.Gui.PlaySound("menubutton_press", false, 1f);
        RequestOutputCraftNow(capi, showError: true);
    }

    private static bool RequestOutputCraftNow(ICoreClientAPI capi, bool showError)
    {
        if (!CanUseCraftingActions(capi))
        {
            return false;
        }

        if (!HasCraftingGrid(capi, out IInventory? craftingGrid))
        {
            return false;
        }

        TransferOutputWithRetries(capi, craftingGrid!, OutputTransferAttempts, moved =>
        {
            if (!moved && showError)
            {
                capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-outputblocked", Lang.Get(ModIds.ModId + ":outputblocked"));
            }
        });

        return true;
    }

    private static bool CraftAllImmediate(ICoreClientAPI capi, GridRecipe[] recipes)
    {
        bool craftedAny = false;

        for (int cycles = 0; cycles < MaxCraftAllCycles; cycles++)
        {
            if (!CanUseCraftingActions(capi))
            {
                return true;
            }

            if (!HasCraftingGrid(capi, out IInventory? craftingGrid))
            {
                return true;
            }

            CraftFillResult fillResult = RecipeGridFiller.TryFillMax(capi, recipes);
            if (fillResult != CraftFillResult.Success && fillResult != CraftFillResult.AlreadyFull)
            {
                if (!craftedAny)
                {
                    HandleFillResult(capi, fillResult);
                }

                return craftedAny;
            }

            string inputSignatureBeforeCraft = GetInputSignature(craftingGrid!);
            if (!TryTransferReadyOutput(capi, craftingGrid!))
            {
                if (!craftedAny)
                {
                    capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-outputblocked", Lang.Get(ModIds.ModId + ":outputblocked"));
                }

                return craftedAny;
            }

            craftedAny = true;

            if (GetInputSignature(craftingGrid!) == inputSignatureBeforeCraft)
            {
                return true;
            }
        }

        capi.TriggerIngameError(typeof(HandbookSpaceCrafting), "quickcraft-craftalllimit", Lang.Get(ModIds.ModId + ":craftalllimit"));
        return true;
    }

    private static bool TryTransferReadyOutput(ICoreClientAPI capi, IInventory craftingGrid)
    {
        RefreshCraftingOutput(craftingGrid);

        ItemSlot outputSlot = craftingGrid[9];
        if (outputSlot == null || outputSlot.Empty || outputSlot.StackSize <= 0)
        {
            return false;
        }

        IClientPlayer player = capi.World.Player;
        IPlayerInventoryManager manager = player.InventoryManager;

        if (!manager.MouseItemSlot.Empty || craftingGrid is not InventoryBase inventoryBase)
        {
            return false;
        }

        ItemStackMoveOperation op = new(capi.World, EnumMouseButton.Left, EnumModifierKey.SHIFT, EnumMergePriority.AutoMerge, outputSlot.StackSize)
        {
            ActingPlayer = player
        };

        object? packet = inventoryBase.ActivateSlot(9, manager.MouseItemSlot, ref op);
        SendPacket(capi, packet);
        return op.MovedQuantity > 0;
    }

    private static long RegisterMenuCallback(ICoreClientAPI capi, Action<float> callback, int delayMs)
    {
        return capi.Event.RegisterCallback(callback, delayMs, permittedWhilePaused: true);
    }

    private static void RefreshCraftingOutput(IInventory craftingGrid)
    {
        try
        {
            craftingGrid.GetType()
                .GetMethod("FindMatchingRecipe", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(craftingGrid, null);
        }
        catch
        {
            // The server will still validate and craft from the activate-slot packet.
        }
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
