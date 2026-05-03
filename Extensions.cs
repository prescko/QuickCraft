using Vintagestory.API.Client;

namespace QuickHandbookCraft;

internal static class Extensions
{
    private static readonly int[] ShiftKeys = { (int)GlKeys.ShiftLeft, (int)GlKeys.ShiftRight };

    public static bool ShiftHeld(this IInputAPI input)
    {
        return ShiftKeys.Any(key => key >= 0 && key < input.KeyboardKeyStateRaw.Length && input.KeyboardKeyStateRaw[key]);
    }
}
