using HarmonyLib;

namespace BetterBackpack;

[HarmonyPatch(typeof(LootableCorpse), nameof(LootableCorpse.TakeFrom), typeof(BaseEntity), typeof(ItemContainer), typeof(ItemContainer), typeof(ItemContainer))]
internal class LootableCorpse_TakeFrom_Patch
{
    [HarmonyPostfix]
    private static void Postfix(LootableCorpse __instance, BaseEntity fromEntity)
    {
        if (!LootDebug.IsActive) return;
        var player = fromEntity as BasePlayer;
        if (!LootDebug.ShouldLog(player) || __instance?.containers == null) return;

        for (int i = 0; i < __instance.containers.Length; i++)
        {
            var name = i == 0 ? "corpse-main" : i == 1 ? "corpse-wear" : i == 2 ? "corpse-belt" : "corpse-" + i;
            LootDebug.Log(player, $"  {name}: {LootDebug.ContentsList(__instance.containers[i])}");
        }
    }
}
