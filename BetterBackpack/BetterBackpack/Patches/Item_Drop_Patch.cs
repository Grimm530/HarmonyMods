using HarmonyLib;
using UnityEngine;

namespace BetterBackpack;

[HarmonyPatch(typeof(Item), nameof(Item.Drop), typeof(Vector3), typeof(Vector3), typeof(Quaternion))]
internal class Item_Drop_Patch
{
    [HarmonyPrefix]
    private static void Prefix(Item __instance)
    {
        if (!LootDebug.IsActive || __instance == null || !__instance.IsBackpack()) return;
        var player = __instance.GetOwnerPlayer();
        if (player == null)
            player = __instance.parent?.playerOwner;
        if (!LootDebug.ShouldLog(player)) return;
        LootDebug.Log(player, $"Item.Drop backpack {__instance.info?.shortname} uid={__instance.uid.Value} | {LootDebug.ContentsList(__instance.contents)}");
    }
}
