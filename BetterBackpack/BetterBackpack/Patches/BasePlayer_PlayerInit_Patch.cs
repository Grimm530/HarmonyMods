using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

/// <summary>
/// When a player initializes (spawns, rejoins), force Main inventory sync so the client
/// receives backpack items for crafting/reload. Delayed so inventory is fully loaded.
/// </summary>
[HarmonyPatch(typeof(BasePlayer), nameof(BasePlayer.PlayerInit))]
internal class BasePlayer_PlayerInit_Patch
{
    [HarmonyPostfix]
    private static void Postfix(BasePlayer __instance)
    {
        if (__instance == null || __instance.IsNpc || __instance.net?.connection == null) return;
        LootDebug.LogPlayerSpawn(__instance);
        var player = __instance;
        InvokeHandler.Invoke(player, () =>
        {
            if (player == null || player.IsDestroyed) return;
            if (BetterBackpackMod.Instance == null) return;
            var prefs = BetterBackpackMod.Instance.GetOrCreatePrefs(player);
            if (prefs == null || !prefs.RetrievalEnabled) return;
            var backpack = player.inventory?.GetBackpackWithInventory();
            if (backpack?.contents == null || backpack.contents.itemList == null || backpack.contents.itemList.Count == 0) return;
            BetterBackpackMod.ForceMainInventorySync(player);
        }, 0.5f);
    }
}
