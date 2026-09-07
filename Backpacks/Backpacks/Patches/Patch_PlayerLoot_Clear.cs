using HarmonyLib;

namespace Backpacks.Patches
{
    [HarmonyPatch(typeof(PlayerLoot), nameof(PlayerLoot.Clear))]
    public static class Patch_PlayerLoot_Clear
    {
        static void Prefix(PlayerLoot __instance)
        {
            BackpacksMod.Instance?.SaveBackpackWhenLootClosed(__instance);
        }
    }
}
