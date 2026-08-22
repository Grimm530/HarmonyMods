using HarmonyLib;
using DHPlugin = Oxide.Plugins.DefendableHomes;

namespace DefendableHomes.Patches
{
    [HarmonyPatch(typeof(LootContainer), nameof(LootContainer.SpawnLoot))]
    public static class Patch_LootContainer_SpawnLoot
    {
        [HarmonyPostfix]
        public static void Postfix(LootContainer __instance)
        {
            DHPlugin.Dispatch_OnLootSpawn(__instance);
        }
    }
}
